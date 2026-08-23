using AVFoundation;
using Foundation;
using KyoshinEewViewer.Core;
using KyoshinEewViewer.Services.Audio;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KyoshinEewViewer.iOS.Audio;

/// <summary>
/// AVFoundation の <see cref="AVAudioPlayer"/> を使った iOS 向けの音声再生バックエンド。
///
/// ManagedBass は BASS のネイティブを動的ロードする前提だが、BASS の iOS 版は静的ライブラリしか
/// 配布されておらず <c>DllImport("bass")</c> を解決できない。そのため OS 標準の AVFoundation を使う。
/// </summary>
public sealed class IosAudioBackend : IAudioBackend
{
	private readonly Lock _lock = new();
	private readonly List<IosAudioChannel> _channels = [];
	private double _globalVolume = 1;
	private int _activeChannelCount;

	public string LastError { get; private set; } = "";

	public bool Initialize()
	{
		// 防災通知は消音スイッチやおやすみモードでも鳴らす必要があるため Playback を選ぶ。
		// Ambient / SoloAmbient は消音スイッチで無音になるため使えない。
		// ただし他アプリの再生を止めてしまうのは望ましくないので DuckOthers を併用し、
		// 自セッションがアクティブな間だけ他アプリの音量を下げる
		// (DuckOthers は MixWithOthers を暗黙に含み、Playback / PlayAndRecord / MultiRoute でのみ指定できる)
		if (AVAudioSession.SharedInstance().SetCategory(AVAudioSessionCategory.Playback, AVAudioSessionCategoryOptions.DuckOthers) is { } error)
		{
			// カテゴリの設定に失敗しても既定のカテゴリで鳴る可能性は残るため、初期化自体は成功扱いにする
			LastError = error.LocalizedDescription;
			AppLog.Default.LogWarning("AVAudioSession のカテゴリ設定に失敗しました: {Error}", error.LocalizedDescription);
		}
		return true;
	}

	public void SetGlobalVolume(double volume)
	{
		lock (_lock)
		{
			_globalVolume = Math.Clamp(volume, 0, 1);
			foreach (var channel in _channels)
				channel.ApplyGlobalVolume(_globalVolume);
		}
	}

	public IAudioChannel? CreateChannel(string filePath)
	{
		var url = NSUrl.FromFilename(filePath);
		var player = AVAudioPlayer.FromUrl(url, out var error);
		if (player is null)
		{
			LastError = error?.LocalizedDescription ?? "AVAudioPlayer を生成できませんでした";
			return null;
		}

		var channel = new IosAudioChannel(this, player);
		lock (_lock)
		{
			_channels.Add(channel);
			channel.ApplyGlobalVolume(_globalVolume);
		}
		// 初回再生の遅延を抑えるためデコーダとバッファを先に用意する
		player.PrepareToPlay();
		return channel;
	}

	public void Dispose()
	{
		IosAudioChannel[] channels;
		lock (_lock)
		{
			channels = [.. _channels];
			_channels.Clear();
		}
		foreach (var channel in channels)
			channel.Dispose();
		GC.SuppressFinalize(this);
	}

	private void RemoveChannel(IosAudioChannel channel)
	{
		lock (_lock)
			_channels.Remove(channel);
	}

	/// <summary>
	/// 再生中のチャンネルが 1 つ以上ある間だけセッションをアクティブにする。
	/// ダッキングはセッションのアクティブ化と同時に始まるため、鳴っていない間も
	/// アクティブにしたままだと他アプリの音量を下げ続けてしまう
	/// </summary>
	private void ActivateSession()
	{
		lock (_lock)
		{
			if (_activeChannelCount++ > 0)
				return;
			if (AVAudioSession.SharedInstance().SetActive(true) is { } error)
				AppLog.Default.LogWarning("AVAudioSession の有効化に失敗しました: {Error}", error.LocalizedDescription);
		}
	}

	private void DeactivateSession()
	{
		lock (_lock)
		{
			if (_activeChannelCount <= 0 || --_activeChannelCount > 0)
				return;
			// NotifyOthersOnDeactivation を付けないと他アプリが音量を戻すきっかけを得られない
			if (AVAudioSession.SharedInstance().SetActive(false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation) is { } error)
				AppLog.Default.LogWarning("AVAudioSession の無効化に失敗しました: {Error}", error.LocalizedDescription);
		}
	}

	private sealed class IosAudioChannel : IAudioChannel
	{
		private readonly IosAudioBackend _backend;
		private readonly AVAudioPlayer _player;
		private readonly Lock _lock = new();
		private double _volume = 1;
		private double _globalVolume = 1;
		private Action? _endCallback;
		private bool _sessionActivated;
		private bool _disposed;

		public IosAudioChannel(IosAudioBackend backend, AVAudioPlayer player)
		{
			_backend = backend;
			_player = player;
			_player.FinishedPlaying += OnFinishedPlaying;
		}

		public double Volume
		{
			get => _volume;
			set {
				_volume = value;
				ApplyVolume();
			}
		}

		public bool IsStopped => !_player.Playing;

		internal void ApplyGlobalVolume(double globalVolume)
		{
			_globalVolume = globalVolume;
			ApplyVolume();
		}

		// AVAudioPlayer には BASS の GlobalStreamVolume に相当する仕組みが無いため、
		// チャンネル音量へ全体音量を掛け込んで反映する。
		// AVAudioPlayer.Volume は 0-1 のみで BASS のような 1 超の増幅はできない
		private void ApplyVolume()
		{
			if (_disposed)
				return;
			_player.Volume = (float)Math.Clamp(_volume * _globalVolume, 0, 1);
		}

		public bool Play()
		{
			if (_disposed)
				return false;

			// 終端まで再生された AVAudioPlayer は CurrentTime が終端に留まるため、
			// 停止状態からの再生は先頭に戻してから始める (BASS は終端で自動的に先頭へ戻る)
			if (!_player.Playing)
				_player.CurrentTime = 0;

			ActivateSession();
			if (_player.Play())
				return true;

			_backend.LastError = "AVAudioPlayer.Play が false を返しました";
			DeactivateSession();
			return false;
		}

		public void Rewind() => _player.CurrentTime = 0;

		public void SlideVolumeToZero(TimeSpan duration)
			=> _player.SetVolume(0, duration.TotalSeconds);

		public void SetEndCallback(Action callback)
		{
			lock (_lock)
				_endCallback = callback;
		}

		public void Dispose()
		{
			lock (_lock)
			{
				if (_disposed)
					return;
				_disposed = true;
				_endCallback = null;
			}
			_player.FinishedPlaying -= OnFinishedPlaying;
			_player.Stop();
			DeactivateSession();
			_backend.RemoveChannel(this);
			_player.Dispose();
			GC.SuppressFinalize(this);
		}

		private void ActivateSession()
		{
			lock (_lock)
			{
				if (_sessionActivated)
					return;
				_sessionActivated = true;
			}
			_backend.ActivateSession();
		}

		private void DeactivateSession()
		{
			lock (_lock)
			{
				if (!_sessionActivated)
					return;
				_sessionActivated = false;
			}
			_backend.DeactivateSession();
		}

		private void OnFinishedPlaying(object? sender, AVStatusEventArgs e)
		{
			DeactivateSession();

			Action? callback;
			lock (_lock)
			{
				callback = _endCallback;
				_endCallback = null;
			}
			if (callback is null)
				return;

			// コールバックはチャンネルを破棄することがあるため、AVAudioPlayer のデリゲート内で
			// 実行しないようスレッドプールへ逃がす (BASS も再生用スレッドから呼ばれる)
			Task.Run(callback);
		}
	}
}
