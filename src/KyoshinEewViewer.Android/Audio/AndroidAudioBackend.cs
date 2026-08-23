using Android.App;
using Android.Content;
using Android.Media;
using KyoshinEewViewer.Core;
using KyoshinEewViewer.Services.Audio;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace KyoshinEewViewer.Android.Audio;

/// <summary>
/// <see cref="MediaPlayer"/> を使った Android 向けの音声再生バックエンド。
///
/// ManagedBass は BASS のネイティブを動的ロードする前提だが、BASS の Android 版のネイティブは
/// APK に同梱されておらず <c>DllImport("bass")</c> を解決できない。そのため OS 標準の
/// MediaPlayer を使う。
/// </summary>
public sealed class AndroidAudioBackend : IAudioBackend
{
	private readonly AudioAttributes _audioAttributes;
	private readonly AudioFocusRequestClass _audioFocusRequest;
	private readonly Lock _lock = new();
	private readonly List<AndroidAudioChannel> _channels = [];
	private AudioManager? _audioManager;
	private double _globalVolume = 1;
	private int _activeChannelCount;

	public AndroidAudioBackend()
	{
		// 防災通知はマナーモードやおやすみモードでも鳴らす必要があるため、アラーム音量で鳴る
		// Alarm を使う (Notification / Media はマナーモードで無音になる)。
		// 内容は警報音や読み上げなので、通知音向けの Sonification とする
		_audioAttributes = new AudioAttributes.Builder()
			.SetUsage(AudioUsageKind.Alarm)!
			.SetContentType(AudioContentType.Sonification)!
			.Build()!;
		// 他アプリの再生を止めてしまわないよう GainTransientMayDuck で要求し、鳴っている間だけ
		// 相手の音量を下げてもらう (iOS の AVAudioSession の DuckOthers 相当)
		_audioFocusRequest = new AudioFocusRequestClass.Builder(AudioFocus.GainTransientMayDuck)!
			.SetAudioAttributes(_audioAttributes)!
			.Build()!;
	}

	public string LastError { get; private set; } = "";

	public bool Initialize()
	{
		_audioManager = Application.Context.GetSystemService(Context.AudioService) as AudioManager;
		if (_audioManager is null)
			AppLog.Default.LogWarning("AudioManager を取得できませんでした。他アプリの音量を下げる制御は行われません");
		// MediaPlayer 自体は AudioManager が取れなくても鳴らせるため、初期化は成功扱いにする
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
		var player = new MediaPlayer();
		try
		{
			// 属性はデータソースの設定より前に指定する必要がある
			player.SetAudioAttributes(_audioAttributes);
			player.SetDataSource(filePath);
			// ローカルファイルのみを扱うため同期版で十分。初回再生の遅延もここで済ませられる
			player.Prepare();
		}
		catch (Exception ex)
		{
			// ファイルが存在しない・読み取り権限が無い・非対応の形式などはいずれも例外で通知される
			LastError = ex.Message;
			player.Release();
			player.Dispose();
			return null;
		}

		var channel = new AndroidAudioChannel(this, player);
		lock (_lock)
		{
			_channels.Add(channel);
			channel.ApplyGlobalVolume(_globalVolume);
		}
		return channel;
	}

	public void Dispose()
	{
		AndroidAudioChannel[] channels;
		lock (_lock)
		{
			channels = [.. _channels];
			_channels.Clear();
		}
		foreach (var channel in channels)
			channel.Dispose();
		GC.SuppressFinalize(this);
	}

	private void RemoveChannel(AndroidAudioChannel channel)
	{
		lock (_lock)
			_channels.Remove(channel);
	}

	/// <summary>
	/// 再生中のチャンネルが 1 つ以上ある間だけ音声フォーカスを保持する。
	/// ダッキングはフォーカスの取得と同時に始まるため、鳴っていない間も保持したままだと
	/// 他アプリの音量を下げ続けてしまう
	/// </summary>
	private void RequestAudioFocus()
	{
		lock (_lock)
		{
			if (_activeChannelCount++ > 0)
				return;
			if (_audioManager is null)
				return;
			if (_audioManager.RequestAudioFocus(_audioFocusRequest) != AudioFocusRequest.Granted)
				AppLog.Default.LogWarning("音声フォーカスの取得に失敗しました");
		}
	}

	private void AbandonAudioFocus()
	{
		lock (_lock)
		{
			if (_activeChannelCount <= 0 || --_activeChannelCount > 0)
				return;
			if (_audioManager is null)
				return;
			_audioManager.AbandonAudioFocusRequest(_audioFocusRequest);
		}
	}

	private sealed class AndroidAudioChannel : IAudioChannel
	{
		/// <summary>
		/// フェードアウトで音量を更新する間隔
		/// </summary>
		private static readonly TimeSpan FadeInterval = TimeSpan.FromMilliseconds(20);

		private readonly AndroidAudioBackend _backend;
		private readonly MediaPlayer _player;
		private readonly Lock _lock = new();
		private double _volume = 1;
		private double _globalVolume = 1;
		private Action? _endCallback;
		private bool _focusHeld;
		private bool _disposed;

		public AndroidAudioChannel(AndroidAudioBackend backend, MediaPlayer player)
		{
			_backend = backend;
			_player = player;
			_player.Completion += OnCompletion;
			_player.Error += OnError;
		}

		public double Volume
		{
			get => _volume;
			set {
				lock (_lock)
				{
					_volume = value;
					ApplyVolume();
				}
			}
		}

		public bool IsStopped
		{
			get {
				lock (_lock)
					return _disposed || !_player.IsPlaying;
			}
		}

		internal void ApplyGlobalVolume(double globalVolume)
		{
			lock (_lock)
			{
				_globalVolume = globalVolume;
				ApplyVolume();
			}
		}

		// MediaPlayer には BASS の GlobalStreamVolume に相当する仕組みが無いため、
		// チャンネル音量へ全体音量を掛け込んで反映する。
		// MediaPlayer.SetVolume は 0-1 のみで BASS のような 1 超の増幅はできない
		private void ApplyVolume()
		{
			if (_disposed)
				return;
			var volume = (float)Math.Clamp(_volume * _globalVolume, 0, 1);
			_player.SetVolume(volume, volume);
		}

		public bool Play()
		{
			// 鳴っている間だけフォーカスを保持するため、開始に失敗したらすぐ手放す
			RequestAudioFocus();
			if (TryStart())
				return true;
			AbandonAudioFocus();
			return false;
		}

		private bool TryStart()
		{
			lock (_lock)
			{
				if (_disposed)
					return false;
				try
				{
					// 終端まで再生された MediaPlayer は再生位置が終端に留まるため、
					// 停止状態からの再生は先頭に戻してから始める (BASS は終端で自動的に先頭へ戻る)
					if (!_player.IsPlaying)
						_player.SeekTo(0);
					_player.Start();
					return true;
				}
				catch (Exception ex)
				{
					_backend.LastError = ex.Message;
					return false;
				}
			}
		}

		public void Rewind()
		{
			lock (_lock)
			{
				if (!_disposed)
					_player.SeekTo(0);
			}
		}

		public void SlideVolumeToZero(TimeSpan duration)
		{
			double startVolume;
			lock (_lock)
			{
				if (_disposed)
					return;
				startVolume = _volume;
				if (duration <= TimeSpan.Zero)
				{
					_volume = 0;
					ApplyVolume();
					return;
				}
			}
			_ = FadeOutAsync(startVolume, duration);
		}

		/// <summary>
		/// 一定間隔で音量を書き換えてフェードアウトを近似する。
		/// MediaPlayer.SetVolume には BASS の ChannelSlideAttribute のような時間指定が無い
		/// </summary>
		private async Task FadeOutAsync(double startVolume, TimeSpan duration)
		{
			var stopwatch = Stopwatch.StartNew();
			while (true)
			{
				await Task.Delay(FadeInterval).ConfigureAwait(false);
				var remain = 1 - Math.Clamp(stopwatch.Elapsed / duration, 0, 1);
				lock (_lock)
				{
					// 破棄済みの MediaPlayer を触ると例外になるため、判定と音量の反映は同じロック内で行う
					if (_disposed)
						return;
					// 複数回呼ばれた場合に音量が上がってしまわないよう、下げる方向にのみ反映する
					_volume = Math.Min(_volume, startVolume * remain);
					ApplyVolume();
				}
				if (remain <= 0)
					return;
			}
		}

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
			// ここより後に MediaPlayer を触る処理は _disposed の判定で弾かれる。
			// 判定は同じロック内で行っているため、解放と同時に音量を書き換えてしまうことはない
			_player.Completion -= OnCompletion;
			_player.Error -= OnError;
			// Release はどの状態からでも呼べるうえ、再生中であれば停止も行う
			_player.Release();
			_player.Dispose();
			AbandonAudioFocus();
			_backend.RemoveChannel(this);
			GC.SuppressFinalize(this);
		}

		// 音声フォーカスの参照数はバックエンド側で管理しているため、チャンネルの
		// ロックを保持したまま呼び出さないこと (ロックの獲得順が逆転する)
		private void RequestAudioFocus()
		{
			lock (_lock)
			{
				if (_focusHeld)
					return;
				_focusHeld = true;
			}
			_backend.RequestAudioFocus();
		}

		private void AbandonAudioFocus()
		{
			lock (_lock)
			{
				if (!_focusHeld)
					return;
				_focusHeld = false;
			}
			_backend.AbandonAudioFocus();
		}

		private void OnCompletion(object? sender, EventArgs e)
		{
			AbandonAudioFocus();

			Action? callback;
			lock (_lock)
			{
				callback = _endCallback;
				_endCallback = null;
			}
			if (callback is null)
				return;

			// コールバックはチャンネルを破棄することがあるため、MediaPlayer のコールバック
			// (メインスレッドの Looper) 上で実行しないようスレッドプールへ逃がす
			Task.Run(callback);
		}

		private void OnError(object? sender, MediaPlayer.ErrorEventArgs e)
		{
			AppLog.Default.LogWarning("音声の再生中にエラーが発生しました。 What:{What} Extra:{Extra}", e.What, e.Extra);
			// 処理済みとして扱わないことで Completion も呼ばれ、通常の終了処理でチャンネルが解放される。
			// (処理済みにすると Completion が呼ばれず、再生完了待ちが終わらなくなる)
			e.Handled = false;
		}
	}
}
