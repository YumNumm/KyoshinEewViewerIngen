using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using KyoshinEewViewer.Core;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Core.Models.Events;
using KyoshinEewViewer.DCReportParser;
using KyoshinEewViewer.Series;
using KyoshinEewViewer.Series.Qzss.Events;
using KyoshinEewViewer.Services;
using KyoshinEewViewer.Services.EqMonitor;
using KyoshinEewViewer.Services.ExternalPublishers.Axis;
using KyoshinEewViewer.Services.Feedback;
using KyoshinEewViewer.Services.TelegramPublishers.Dmdata;
using KyoshinEewViewer.Services.TelegramPublishers.JmaXml;
using KyoshinEewViewer.Services.Voicevox;
using KyoshinEewViewer.Services.Workflows;
using KyoshinEewViewer.Services.Workflows.BuiltinActions;
using KyoshinEewViewer.Views.SettingPages;
using KyoshinMonitorLib;
using R3;
using Scriban.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace KyoshinEewViewer.ViewModels;

public partial class SettingWindowViewModel : NavigationPaneViewModelBase
{
	/// <summary>
	/// 設定項目の一覧のペインは幅 200px あるため、設定内容側に狭い端末の画面幅と同程度 (360px) を残せる幅を下限とする
	/// 設定ウィンドウの MinWidth (640px) より小さいため、デスクトップでは常に一覧を表示したままになる
	/// </summary>
	protected override double PaneVisibleMinWidth => 560;

	public static Dictionary<KyoshinEventLevel, string> KyoshinEventLevelNames { get; } = new()
	{
		{ KyoshinEventLevel.Weaker, "微弱(非推奨)" },
		{ KyoshinEventLevel.Weak, "弱い(震度1未満)" },
		{ KyoshinEventLevel.Medium, "普通(震度1程度以上)" },
		{ KyoshinEventLevel.Strong, "強い(震度3程度以上)" },
		{ KyoshinEventLevel.Stronger, "非常に強い(震度5弱程度以上)" },
		{ KyoshinEventLevel.Disabled, "利用しない" },
	};
	/// <summary>
	/// 地点予測を展開表示する震度のしきい値の選択肢
	/// </summary>
	public static Dictionary<JmaIntensity, string> PointForecastExpandIntensityNames { get; } = new()
	{
		{ JmaIntensity.Int1, "震度1以上" },
		{ JmaIntensity.Int2, "震度2以上" },
		{ JmaIntensity.Int3, "震度3以上" },
		{ JmaIntensity.Int4, "震度4以上" },
		{ JmaIntensity.Int5Lower, "震度5弱以上" },
		{ JmaIntensity.Int5Upper, "震度5強以上" },
		{ JmaIntensity.Int6Lower, "震度6弱以上" },
		{ JmaIntensity.Int6Upper, "震度6強以上" },
		{ JmaIntensity.Int7, "震度7" },
	};
	public static Dictionary<KyoshinEewViewerConfiguration.KyoshinMonitorConfig.Mode, string> KyoshinMonitorModeNames { get; } = new()
	{
		{ KyoshinEewViewerConfiguration.KyoshinMonitorConfig.Mode.None, "受信しない" },
		{ KyoshinEewViewerConfiguration.KyoshinMonitorConfig.Mode.Kmoni, "強震モニタ" },
		{ KyoshinEewViewerConfiguration.KyoshinMonitorConfig.Mode.Lmoni, "長周期地震動モニタ" },
	};
	public static Dictionary<ShakeDetectionDisplayMode, string> ShakeDetectionDisplayModeNames { get; } = new()
	{
		{ ShakeDetectionDisplayMode.None, "表示しない" },
		{ ShakeDetectionDisplayMode.Grid, "グリッド" },
		{ ShakeDetectionDisplayMode.ConvexHull, "凸包(非推奨)" },
	};
	public static Dictionary<ShakeDetectionAnimationMode, string> ShakeDetectionAnimationModeNames { get; } = new()
	{
		{ ShakeDetectionAnimationMode.None, "アニメーションなし" },
		{ ShakeDetectionAnimationMode.Blink, "点滅" },
		{ ShakeDetectionAnimationMode.Pulse, "明滅" },
	};

	public KyoshinEewViewerConfiguration Config { get; }
	public SeriesController SeriesController { get; }
	public SoundPlayerService SoundPlayerService { get; }
	public UpdateCheckService UpdateCheckService { get; }
	public WorkflowService WorkflowService { get; }
	public VoicevoxService VoicevoxService { get; }
	public ISubWindowsService? SubWindowService { get; }

	private ILogger Logger { get; }

	private ISettingPage _selectedSettingPage;
	public ISettingPage SelectedSettingPage
	{
		get => _selectedSettingPage;
		set {
			var oldValue = _selectedSettingPage;
			SetProperty(ref _selectedSettingPage, value);
			if (value is BasicSettingPage && oldValue is not BasicSettingPage)
			{
				SelectedSettingPage = oldValue;
				return;
			}
		}
	}
	private BasicSettingPage<UpdatePage> UpdatePage { get; }
	private BasicSettingPage<DebugMenuPage> DebugMenuPage { get; }
	public ISettingPage[] SettingPages { get; }

	public SettingWindowViewModel(
		KyoshinEewViewerConfiguration config,
		SeriesController seriesController,
		UpdateCheckService updateCheckService,
		SoundPlayerService soundPlayerService,
		WorkflowService workflowService,
		VoicevoxService voicevoxService,
		ILogger<SettingWindowViewModel> logger,
		DmdataSettingPage dmdataPage,
		AxisSettingPage axisPage,
		JmaXmlSettingPage jmaXmlPage,
		EqMonitorSettingPage eqMonitorPage,
		FeedbackSettingPage feedbackPage,
		DmdataRedundantTelegramPublisher? dmdataPublisher = null,
		ISubWindowsService? subWindowService = null)
	{
		Config = config;
		SeriesController = seriesController ?? throw new ArgumentNullException(nameof(seriesController));
		UpdateCheckService = updateCheckService;
		SoundPlayerService = soundPlayerService;
		WorkflowService = workflowService;
		VoicevoxService = voicevoxService;
		SubWindowService = subWindowService;
		DmdataPublisher = dmdataPublisher ?? ServiceLocator.Current.GetService<DmdataRedundantTelegramPublisher>();

		Logger = logger;

		Series = SeriesController.AllSeries.Select(s => new SeriesViewModel(s, Config)).ToArray();

		RegisteredSounds = SoundPlayerService.RegisteredSounds.Select(s => new SoundConfigViewModel(s.Key, s.Value)).ToArray();
		OpenSoundFile = new AsyncRelayCommand<KyoshinEewViewerConfiguration.SoundConfig>(async config =>
		{
			if (await PickSoundFileAsync() is { } path)
				config.FilePath = path;
		});

		ResetMapPosition = new RelayCommand(() =>
		{
			Config.Map.Location1 = new(45.619358f, 145.77399f);
			Config.Map.Location2 = new(29.997368f, 128.22534f);
		});

		updateCheckService.Updated += a =>
		{
			VersionInfos = a;
			if ((a?.Length ?? 0) > 0 && UpdatePage != null)
			{
				UpdatePage.IsVisible = true;
				SelectedSettingPage = UpdatePage;
			}
		};
		VersionInfos = updateCheckService.AvailableUpdateVersions;

		updateCheckService.ObservePropertyChanged(x => x.IsUpdateIndeterminate).Subscribe(x => IsUpdateIndeterminate = x);
		updateCheckService.ObservePropertyChanged(x => x.UpdateProgress).Subscribe(x => UpdateProgress = x);
		updateCheckService.ObservePropertyChanged(x => x.UpdateProgressMax).Subscribe(x => UpdateProgressMax = x);
		updateCheckService.ObservePropertyChanged(x => x.UpdateState).Subscribe(x => UpdateState = x);

		SelectedWorkflow = WorkflowService.Workflows.FirstOrDefault();

		VoicevoxService.ObservePropertyChanged(x => x.Speakers)
			.Subscribe(s => VoicevoxSpeakerName = s.SelectMany(t => t switch
			{
				MultiStyleSpeaker ms => ms.Styles,
				SingleStyleSpeaker ss => [ss],
				_ => [],
			}).FirstOrDefault(s => s.SpeakerId == config.Voicevox.SpeakerId)?.Name ?? "不明");

		UpdatePage = new BasicSettingPage<UpdatePage>("\xf071", "アプリの更新", []) { IsVisible = false };
		// リリースビルドでも設定ボタンの長押しから開けるようにするため、ページ自体は常に用意する
		DebugMenuPage = new BasicSettingPage<DebugMenuPage>("\xf188", "デバッグメニュー", []) { IsVisible = false };
		SettingPages = [
			UpdatePage,
			new BasicSettingPage<GeneralPage>("\xf53f", "外観･基本設定", []),
			new BasicSettingPage<FeaturePage>("\xf085", "機能設定", []),
			new BasicSettingPage<NotifyPage>("\xf075", "通知", []),
			// iOS では Window を生成できないためマルチウィンドウ機能を利用できない
			new BasicSettingPage<MultiWindowPage>("\xf2d2", "マルチウィンドウ", []) { IsVisible = !OperatingSystem.IsIOS() },
			new BasicSettingPage<SoundPage>("\xf028", "音声", []),
			new BasicSettingPage<WorkflowPage>("\xe289", "ワークフロー", []),
			new BasicSettingPage<VoicevoxPage>("\xf075", "VOICEVOX", []),
			..SeriesController.EnabledSeries.SelectMany(s => s.SettingPages),
			new BasicSettingPage("\xf48b", "配信サービス", [
				dmdataPage,
				jmaXmlPage,
				axisPage,
				eqMonitorPage,
			]),
			new BasicSettingPage<MapPage>("\xf5a0", "地図", []),
			feedbackPage,
			new BasicSettingPage<AboutPage>("\xf129", "このアプリについて", []),
			new BasicSettingPage<LicencePage>("\xf2c2", "ライセンス", []),
			DebugMenuPage,
		];
		_selectedSettingPage = SettingPages[1];
		if ((updateCheckService.AvailableUpdateVersions?.Length ?? 0) > 0)
		{
			UpdatePage.IsVisible = true;
			SelectedSettingPage = UpdatePage;
		}

		if (Design.IsDesignMode)
		{
			IsDebug = true;
			VersionInfos =
			[
				new VersionInfo
				{
					Time = DateTime.Now,
					Message = "test",
					VersionString = "1.1.31.0"
				},
			];
			IsUpdating = true;
			IsUpdateIndeterminate = false;
			UpdateProgressMax = 100;
			UpdateProgress = 50;
			return;
		}
#if DEBUG
		IsDebug = true;
		DebugMenuPage.IsVisible = true;
#endif
	}

	/// <summary>
	/// デバッグメニューを表示して選択する。設定ボタンの長押しから呼ばれる
	/// </summary>
	public void ShowDebugMenu()
	{
		DebugMenuPage.IsVisible = true;
		SelectedSettingPage = DebugMenuPage;
	}

	public string Title { get; } = "設定 - KyoshinEewViewer for ingen";

	[ObservableProperty]
	public partial bool IsDebug { get; set; }

	public List<JmaIntensity> Ints { get; } = [
		JmaIntensity.Unknown,
		JmaIntensity.Int0,
		JmaIntensity.Int1,
		JmaIntensity.Int2,
		JmaIntensity.Int3,
		JmaIntensity.Int4,
		JmaIntensity.Int5Lower,
		JmaIntensity.Int5Upper,
		JmaIntensity.Int6Lower,
		JmaIntensity.Int6Upper,
		JmaIntensity.Int7,
		JmaIntensity.Error,
	];

	public List<LpgmIntensity> LpgmInts { get; } = [
		LpgmIntensity.Unknown,
		LpgmIntensity.LpgmInt0,
		LpgmIntensity.LpgmInt1,
		LpgmIntensity.LpgmInt2,
		LpgmIntensity.LpgmInt3,
		LpgmIntensity.LpgmInt4,
		LpgmIntensity.Error,
	];

	public SeriesViewModel[] Series { get; }

	public bool IsSoundActivated => SoundPlayerService.IsAvailable;
	public SoundConfigViewModel[] RegisteredSounds { get; }

	[ObservableProperty]
	public partial Workflow? SelectedWorkflow { get; set; }

	/// <summary>
	/// ファイルピッカーが返したファイルを、自身のコンテナへ複製しないと設定として保存できない
	/// プラットフォームかどうか
	/// </summary>
	private static bool NeedsSoundFileLocalization => OperatingSystem.IsIOS() || OperatingSystem.IsAndroid();

	/// <summary>
	/// 音声ファイルを選択させ、設定に保存できるパスを返す
	/// </summary>
	/// <returns>設定に保存するパス。選択されなかった場合や保存できるパスが得られなかった場合は null</returns>
	private static async Task<string?> PickSoundFileAsync()
	{
		if (KyoshinEewViewerApp.TopLevelControl == null)
			return null;
		var files = await KyoshinEewViewerApp.TopLevelControl.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
		{
			Title = "音声ファイルを開く",
			FileTypeFilter = [FilePickerFileTypes.All],
			AllowMultiple = false,
		});
		if (files is not { Count: > 0 })
			return null;

		// Android のピッカーは content:// の URI しか返さないため、ローカルパスは常に取得できない。
		// 複製で解決できるプラットフォームではパスが無くても続行する
		var localPath = files[0].TryGetLocalPath();
		if (localPath is null && !NeedsSoundFileLocalization)
			return null;

		return await LocalizeSoundFileAsync(files[0], localPath);
	}

	/// <remarks>
	/// ファイルピッカーが返すファイルは、設定として保存して後から再生することができない。
	/// iOS は返されるパスが他アプリのコンテナを指しており、選択直後しか開けない。
	/// Android は content:// の URI しか返されず、そもそもローカルパスを取得できない。
	/// いずれも自身のコンテナへ複製しておく必要がある
	/// </remarks>
	/// <returns>設定に保存するパス。複製に失敗し、代わりに保存できるパスも無い場合は null</returns>
	private static async Task<string?> LocalizeSoundFileAsync(IStorageFile file, string? localPath)
	{
		if (!NeedsSoundFileLocalization)
			return localPath;

		try
		{
			var directory = Path.Combine(PlatformDirectories.ApplicationData, "Sounds");
			PlatformDirectories.EnsureDirectoryExists(directory);
			var destination = Path.Combine(directory, file.Name);

			await using var source = await file.OpenReadAsync();
			await using var target = File.Create(destination);
			await source.CopyToAsync(target);
			return destination;
		}
		catch (Exception ex)
		{
			AppLog.Default.LogWarning(ex, "音声ファイルの複製に失敗しました");
			// 複製できなくても、選択直後しか開けないとはいえパスが得られている iOS では従来どおりそれを使う。
			// Android は null のままなので、壊れたパスを設定に保存せず呼び出し側で中断させる
			return localPath;
		}
	}

	public void LoadWorkflows()
	{
		WorkflowService.LoadWorkflows();
		SelectedWorkflow = WorkflowService.Workflows.FirstOrDefault(w => w.Id == SelectedWorkflow?.Id)
			?? WorkflowService.Workflows.FirstOrDefault();
	}
	public void AddWorkflow()
	{
		var wf = new Workflow() { Name = "新しいワークフロー", Trigger = new DummyTrigger() };
		WorkflowService.Workflows.Add(wf);
		SelectedWorkflow = wf;
	}
	public async void RemoveWorkflow(object? parameter)
	{
		if (parameter is not Workflow workflow)
			return;
		var result = await DialogHelper.ShowSettingWindowConfirmationDialogAsync(
			"ワークフローの削除",
			$"ワークフロー「{workflow.Name}」を削除しますか？\nこの操作は元に戻すことができません。");

		if (result)
		{
			WorkflowService.Workflows.Remove(workflow);
			SelectedWorkflow = WorkflowService.Workflows.FirstOrDefault();
		}
	}
	public async Task TestRunWorkflow(object? parameter)
	{
		if (parameter is not Workflow workflow)
			return;
		workflow.IsTestRunning = true;
		try
		{
			await workflow.TestRunAsync();
		}
		catch (ScriptRuntimeException ex)
		{
			// ユーザーが記述したテンプレートの問題のため、Sentry に送信しない
			Logger.LogWarning(ex, "ワークフローのテスト実行中にテンプレートのエラーが発生しました");
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "ワークフローのテスト実行中に例外が発生しました");
		}
		finally
		{
			workflow.IsTestRunning = false;
		}
	}
	public async Task OpenSoundFileForWorkflow(object? parameter)
	{
		if (parameter is not PlaySoundAction action)
			return;
		if (await PickSoundFileAsync() is { } path)
			action.FilePath = path;
	}
	public void OpenWorkflowPage()
		=> UrlOpener.OpenUrl("https://github.com/ingen084/KyoshinEewViewerIngen/blob/develop/workflow-guide.md");


	[ObservableProperty]
	public partial string VoicevoxSpeakerName { get; set; } = "話者一覧が読み込まれていません";
	[ObservableProperty]
	public partial bool IsVoicevoxTestPlaying { get; set; }

	public async Task PlayVoicevoxTestSound()
	{
		try
		{
			IsVoicevoxTestPlaying = true;
			await VoicevoxService.PlayTest();
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "VOICEVOX のテスト再生中に例外が発生しました");
		}
		finally
		{
			IsVoicevoxTestPlaying = false;
		}
	}
	public Task UpdateVoicevoxSpeakers()
		=> VoicevoxService.GetSpeakers();
	public void UpdateVoicevoxSpeaker(object? parameter)
	{
		if (parameter is not SingleStyleSpeaker ss)
			return;
		Config.Voicevox.SpeakerId = ss.SpeakerId;
		VoicevoxSpeakerName = ss.Name;
	}
	public void ClearVoicevoxCache()
	{
		try
		{
			VoicevoxService.ClearCache();
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "VoiceVoxキャッシュのクリア中に例外が発生しました");
		}
	}

	#region Update

	[ObservableProperty]
	public partial VersionInfo[]? VersionInfos { get; set; }

	[ObservableProperty]
	public partial bool UpdaterEnable { get; set; } = true;

	[ObservableProperty]
	public partial bool IsUpdating { get; set; }

	[ObservableProperty]
	public partial bool IsUpdateIndeterminate { get; set; }

	[ObservableProperty]
	public partial double UpdateProgress { get; set; }

	[ObservableProperty]
	public partial double UpdateProgressMax { get; set; }

	[ObservableProperty]
	public partial string UpdateState { get; set; } = "-";

	public void StartUpdater()
	{
		UpdaterEnable = false;
		IsUpdating = true;
		UpdateCheckService.StartUpdater()
			.ContinueWith(_ => UpdaterEnable = true).ConfigureAwait(false);
	}

	public void ForceStartUpdater()
	{
		UpdaterEnable = false;
		IsUpdating = true;
		UpdateCheckService.StartUpdater(forceUpdate: true)
			.ContinueWith(_ => UpdaterEnable = true).ConfigureAwait(false);
	}
	#endregion

	public async Task ResetMultiWindowPositions()
	{
		var result = await DialogHelper.ShowSettingWindowConfirmationDialogAsync(
			"ウィンドウ位置のリセット",
			"すべてのウィンドウの位置設定をリセットします。\n現在開いているウィンドウは閉じられます。\nよろしいですか？");

		if (!result)
			return;

		if (Config.MultiWindow.Enable)
			SubWindowService?.CloseAllSeriesWindows();

		Config.MultiWindow.SeriesWindows.Clear();
	}

	public async Task EditWindowTheme()
	{
		if (SubWindowService == null || KyoshinEewViewerApp.Selector == null)
			return;
		await SubWindowService.ShowDialogWindowThemeEditWindow(KyoshinEewViewerApp.Selector.SelectedWindowTheme);
	}
	public async Task EditIntensityTheme()
	{
		if (SubWindowService == null || KyoshinEewViewerApp.Selector == null)
			return;
		await SubWindowService.ShowDialogIntensityThemeEditWindow(KyoshinEewViewerApp.Selector.SelectedIntensityTheme);
	}

	public bool IsLinux { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
	public bool IsWindows { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
	public bool IsMacOs { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
	public bool IsIOS { get; } = OperatingSystem.IsIOS();
	// トレイアイコンはデスクトップ (Windows/macOS/Linux) で利用できる
	public bool IsDesktop { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
	public bool IsLogDirectoryCustomizable { get; } = PlatformDirectories.IsLogDirectoryCustomizable;
	public bool IsUseCurrentDirectoryOptionAvailable { get; } = !RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
	// macOS は優先度を変更しても効果がなく、iOS はプロセスの優先度を扱えない
	public bool IsProcessPriorityConfigurable { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
	// App Store 配信では自己更新が禁止されているため iOS では更新チェックを行わない
	public bool IsUpdateCheckAvailable { get; } = !OperatingSystem.IsIOS();

	public void OpenLogDirectory()
	{
		string logPath;
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			logPath = PlatformDirectories.Logs;
		}
		else if (Config.Logging.UseCurrentDirectory)
		{
			logPath = Path.IsPathFullyQualified(Config.Logging.Directory)
				? Config.Logging.Directory
				: Path.Combine(Environment.CurrentDirectory, Config.Logging.Directory);
		}
		else
		{
			logPath = Path.IsPathFullyQualified(Config.Logging.Directory)
				? Config.Logging.Directory
				: Path.Combine(PlatformDirectories.ApplicationData, Config.Logging.Directory);
		}

		UrlOpener.OpenUrl(logPath);
	}

	public IRelayCommand RegistMapPosition { get; } = new RelayCommand(() => StrongReferenceMessenger.Default.Send(new RegistMapPositionRequested()));
	public IRelayCommand ResetMapPosition { get; }
	public IRelayCommand<string> OpenUrl { get; } = new RelayCommand<string>(url => { if (url != null) UrlOpener.OpenUrl(url); });

	public IAsyncRelayCommand<KyoshinEewViewerConfiguration.SoundConfig> OpenSoundFile { get; }

	#region debug
	public string CurrentDirectory => Environment.CurrentDirectory;

	[ObservableProperty]
	public partial string ReplayBasePath { get; set; } = "";

	[ObservableProperty]
	public partial DateTimeOffset ReplaySelectedDate { get; set; } = DateTimeOffset.Now;

	[ObservableProperty]
	public partial TimeSpan ReplaySelectedTime { get; set; }

	[ObservableProperty]
	public partial string JmaEqdbId { get; set; } = "20180618075834";
	public void ProcessJmaEqdbRequest()
		=> ProcessJmaEqdbRequested.Request(JmaEqdbId);

	[ObservableProperty]
	public partial string QzqsmHexString { get; set; } = "9AAF8DED25000325BA00DA4A0F5AAC5A8000000008000000200000136DCCFB40";

	public void ProcessDCReportRequest()
	{
		var lines = QzqsmHexString.Split('\n');
		foreach (var line in lines)
		{
			var trimmedLine = line.Trim();
			if (string.IsNullOrWhiteSpace(trimmedLine))
				continue;
			try
			{
				DCReport report;
				if (trimmedLine.StartsWith("$QZQSM"))
				{
					// NMEAセンテンスとしてパース
					report = DCReport.ParseFromNmea(trimmedLine);
				}
				else
				{
					// HEX文字列としてパース
					report = DCReport.Parse(Convert.FromHexString(trimmedLine.Length % 2 != 0 ? trimmedLine + "0" : trimmedLine));
				}
				ProcessManualDCReportRequested.Request(report);
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "デバッグ用DCレポートの解析中にエラーが発生しました");
				System.Diagnostics.Debug.WriteLine($"デバッグ用DCレポートの解析中にエラーが発生しました: {ex.Message}");
			}
		}
	}

	public void CrashApp()
		=> throw new ApplicationException("クラッシュボタンが押下されました。");

	private DmdataRedundantTelegramPublisher? DmdataPublisher { get; }

	public string? WebSocketOverrideUrl
	{
		get => Config.Dmdata.WebSocketDefaultEndpoint;
		set
		{
			Config.Dmdata.WebSocketDefaultEndpoint = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
			OnPropertyChanged();
		}
	}

	public string WebSocketRedundantEndpointsText
	{
		get => string.Join(Environment.NewLine, Config.Dmdata.WebSocketRedundantEndpoints ?? []);
		set
		{
			Config.Dmdata.WebSocketRedundantEndpoints = value
				.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Where(s => !string.IsNullOrWhiteSpace(s))
				.ToArray();
			if (Config.Dmdata.WebSocketRedundantEndpoints.Length == 0)
				Config.Dmdata.WebSocketRedundantEndpoints = null;
			OnPropertyChanged();
		}
	}

	[ObservableProperty]
	public partial bool IsDmdataReconnecting { get; set; }

	[ObservableProperty]
	public partial string? DmdataReconnectStatus { get; set; }

	public async Task ReconnectDmdataAsync()
	{
		if (DmdataPublisher == null)
		{
			DmdataReconnectStatus = "DmdataPublisher が利用できません";
			return;
		}

		IsDmdataReconnecting = true;
		DmdataReconnectStatus = "再接続中...";
		try
		{
			await DmdataPublisher.ForceReconnectAsync();
			DmdataReconnectStatus = "再接続要求を送信しました";
		}
		catch (Exception ex)
		{
			DmdataReconnectStatus = $"エラー: {ex.Message}";
		}
		finally
		{
			IsDmdataReconnecting = false;
		}
	}
	#endregion
}

public record class SeriesViewModel(SeriesMeta Meta, KyoshinEewViewerConfiguration Config)
{
	public bool IsEnabled
	{
		get => Config.SeriesEnable.TryGetValue(Meta.Key, out var e) ? e : Meta.IsDefaultEnabled;
		set => Config.SeriesEnable[Meta.Key] = value;
	}
}

public record class SoundConfigViewModel(SoundCategory Category, List<Sound> Sounds);
