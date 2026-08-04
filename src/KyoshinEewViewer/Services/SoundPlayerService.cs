using KyoshinEewViewer.Core;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Services.Audio;
using ReactiveUI;
using Splat;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace KyoshinEewViewer.Services;

/// <summary>
/// 音声を再生するサービス
/// </summary>
public class SoundPlayerService
{
	internal KyoshinEewViewerConfiguration Config { get; }

	/// <summary>
	/// 利用可能かどうか
	/// </summary>
	public bool IsAvailable { get; }
	public Sound TestSound { get; }
	internal ILogger Logger { get; }
	internal IAudioBackend Backend { get; }

	public SoundPlayerService(KyoshinEewViewerConfiguration config, ILogManager logManager)
	{
		SplatRegistrations.RegisterLazySingleton<SoundPlayerService>();

		Config = config;
		Logger = logManager.GetLogger<SoundPlayerService>();
		// BASS のネイティブが存在しない iOS ではヘッド側が実装を登録している
		Backend = Locator.Current.GetService<IAudioBackend>() ?? new BassAudioBackend();

		// とりあえず初期化を試みる
		try
		{
			//デバイス選べるようにしたい
			//for (var i = 0; i < Bass.DeviceCount; i++)
			//{
			//	var info = Bass.GetDeviceInfo(i);
			//	Logger.LogDebug($"デバイス{i}: {info.Name}");
			//}

			if (IsAvailable = Backend.Initialize())
			{
				void UpdateVolume()
					=> Backend.SetGlobalVolume(Config.Audio.IsMuted ? 0 : Config.Audio.GlobalVolume);

				Config.Audio.WhenAnyValue(x => x.GlobalVolume)
					.Subscribe(_ => UpdateVolume());
				Config.Audio.WhenAnyValue(x => x.IsMuted)
					.Subscribe(_ => UpdateVolume());
				UpdateVolume();
			}
			else
				Logger.LogWarning($"音声バックエンドの初期化に失敗しました。 LastError:{Backend.LastError}");
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "音声バックエンドの初期化に失敗しました");
			IsAvailable = false;
		}
		TestSound = RegisterSound(
			new SoundCategory("Test", "テスト"),
			"TestPlay",
			"揺れ検出(震度1未満)",
			"{test}: 奇数秒|偶数秒\n{!test}: testを反転したもの",
			new()
			{
				{ "test", "奇数秒" },
				{ "!test", "偶数秒" },
			}
		);
	}
	~SoundPlayerService()
	{
		DisposeItems();
		if (IsAvailable)
			Backend.Dispose();
	}

	private Dictionary<SoundCategory, List<Sound>> Sounds { get; } = [];
	public IReadOnlyDictionary<SoundCategory, List<Sound>> RegisteredSounds => Sounds;

	public async Task<bool> PlayAsync(string path, double volume, bool waitToEnd)
	{
		if (!IsAvailable)
		{
			Logger.LogWarning($"音声を再生できません。音声バックエンドが利用できません。 Path:{path}");
			return false;
		}

		var ch = Backend.CreateChannel(path);
		if (ch is null)
		{
			Logger.LogWarning($"音声ストリームの作成に失敗しました。 LastError:{Backend.LastError} Exists:{File.Exists(path)} Path:{path}");
			return false;
		}
		ch.Volume = Math.Clamp(volume, 0, 1);

		var mre = new ManualResetEventSlim(false);
		ch.SetEndCallback(() =>
		{
			ch.Dispose();
			mre.Set();
		});
		if (ch.Play())
		{
			if (waitToEnd)
				await Task.Run(mre.Wait);
			return true;
		}
		Logger.LogWarning($"音声の再生開始に失敗しました。 LastError:{Backend.LastError} Path:{path}");
		ch.Dispose();
		return false;
	}

	public Sound RegisterSound(SoundCategory category, string name, string displayName, string? description = null, Dictionary<string, string>? exampleParameter = null)
	{
		if (Sounds.TryGetValue(category, out var sounds))
		{
			var sound = sounds.FirstOrDefault(s => s.Name == name);
			if (sound is not null)
				return sound;
			sound = new(this, category, name, displayName, description, exampleParameter);
			sounds.Add(sound);
			return sound;
		}
		var sound2 = new Sound(this, category, name, displayName, description, exampleParameter);
		Sounds.Add(category, [sound2]);
		return sound2;
	}

	public void DisposeItems()
	{
		foreach (var s in Sounds.SelectMany(s => s.Value).ToArray())
			s.Dispose();
		Sounds.Clear();
	}
}

public record struct SoundCategory(string Name, string DisplayName);
public class Sound : IDisposable
{
	internal Sound(SoundPlayerService service, SoundCategory parentCategory, string name, string displayName, string? description, IDictionary<string, string>? exampleParameter)
	{
		Service = service;
		ParentCategory = parentCategory;
		Name = name;
		DisplayName = displayName;
		Description = description;
		ExampleParameter = exampleParameter;
	}

	private SoundPlayerService Service { get; }
	public SoundCategory ParentCategory { get; }
	public string Name { get; }
	public string DisplayName { get; }
	public string? Description { get; }
	public IDictionary<string, string>? ExampleParameter { get; }

	// 設定を取得する 存在しなければ項目を作成する
	public KyoshinEewViewerConfiguration.SoundConfig Config
	{
		get {
			KyoshinEewViewerConfiguration.SoundConfig? config;
			if (!Service.Config.Sounds.TryGetValue(ParentCategory.Name, out var sounds))
			{
				config = new();
				Service.Config.Sounds[ParentCategory.Name] = new() { { Name, config } };
				return config;
			}
			if (sounds.TryGetValue(Name, out config))
				return config;

			return sounds[Name] = new();
		}
	}

	private string? LoadedFilePath { get; set; }
	private IAudioChannel? Channel { get; set; }

	private bool IsDisposed { get; set; }

	/// <summary>
	/// 音声を再生する
	/// </summary>
	public bool Play() => Play(null);

	/// <summary>
	/// 音声を再生する
	/// </summary>
	/// <param name="parameters">ファイルパスの置換に使用するパラメータ</param>
	public bool Play(Dictionary<string, string>? parameters)
	{
		var config = Config;

		string GetFilePath()
		{
			if (string.IsNullOrWhiteSpace(config?.FilePath))
				return "";

			var useParams = parameters ?? ExampleParameter;
			if (useParams == null || useParams.Count == 0)
				return config.FilePath;

			// Dictionary の Key を {(key1|key2)} みたいなパターンに置換する
			var pattern = $"{{({string.Join('|', useParams.Select(kvp => Regex.Escape(kvp.Key)))})}}";
			// このパターンを使って置き換え
			return Regex.Replace(config.FilePath, pattern, m => useParams[m.Groups[1].Value]);
		}

		var filePath = GetFilePath();
		if (!config.Enabled || string.IsNullOrWhiteSpace(filePath))
			return false;

		if (!Service.IsAvailable)
		{
			Service.Logger.LogWarning($"音声を再生できません。音声バックエンドが利用できません。 Sound:{ParentCategory.Name}/{Name} Path:{filePath}");
			return false;
		}

		if (IsDisposed)
		{
			Service.Logger.LogWarning($"破棄済みの音声を再生しようとしました。 Sound:{ParentCategory.Name}/{Name} Path:{filePath}");
			return false;
		}

		// AllowMultiPlayが有効な場合クラス内部のキャッシュは使用しない
		// 再生ごとにファイルの読み込み･再生完了時に開放を行う
		if (config.AllowMultiPlay)
		{
			if (Channel is { } cachedChannel)
			{
				cachedChannel.Dispose();
				Channel = null;
				LoadedFilePath = null;
			}
			var ch = Service.Backend.CreateChannel(filePath);
			if (ch is null)
			{
				Service.Logger.LogWarning($"音声ストリームの作成に失敗しました。 LastError:{Service.Backend.LastError} Exists:{File.Exists(filePath)} Sound:{ParentCategory.Name}/{Name} Path:{filePath}");
				return false;
			}
			ch.Volume = Math.Clamp(config.Volume, 0, 1);
			ch.SetEndCallback(ch.Dispose);

			if (ch.Play())
				return true;
			Service.Logger.LogWarning($"音声の再生開始に失敗しました。 LastError:{Service.Backend.LastError} Sound:{ParentCategory.Name}/{Name} Path:{filePath}");
			ch.Dispose();
			return false;
		}

		if (Channel is null || LoadedFilePath != filePath)
		{
			LoadedFilePath = null;
			Channel?.Dispose();
			Channel = Service.Backend.CreateChannel(filePath);
			if (Channel is null)
			{
				Service.Logger.LogWarning($"音声ストリームの作成に失敗しました。 LastError:{Service.Backend.LastError} Exists:{File.Exists(filePath)} Sound:{ParentCategory.Name}/{Name} Path:{filePath}");
				return false;
			}
			LoadedFilePath = filePath;
		}

		var c = Channel;
		c.Volume = Math.Clamp(config.Volume, 0, 1);
		if (!c.IsStopped)
			c.Rewind();
		if (c.Play())
			return true;
		Service.Logger.LogWarning($"音声の再生開始に失敗しました。 LastError:{Service.Backend.LastError} Sound:{ParentCategory.Name}/{Name} Path:{filePath}");
		return false;
	}

	public void Dispose()
	{
		if (Channel is { } i)
		{
			i.Dispose();
			Channel = null;
			LoadedFilePath = null;
		}
		IsDisposed = true;
		GC.SuppressFinalize(this);
	}
}
