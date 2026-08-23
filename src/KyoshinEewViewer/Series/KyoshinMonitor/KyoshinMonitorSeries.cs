using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using FluentAvalonia.UI.Controls;
using KyoshinEewViewer.Core;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Events;
using KyoshinEewViewer.Series.EewHistory;
using KyoshinEewViewer.Series.EewHistory.Models;
using KyoshinEewViewer.Series.KyoshinMonitor.Events;
using KyoshinEewViewer.Series.KyoshinMonitor.Models;
using KyoshinEewViewer.Series.KyoshinMonitor.SettingPages;
using KyoshinEewViewer.Series.KyoshinMonitor.Templates;
using KyoshinEewViewer.Series.KyoshinMonitor.Workflow;
using KyoshinEewViewer.Services;
using KyoshinEewViewer.Services.EqMonitor;
using R3;
using WorkflowsNamespace = KyoshinEewViewer.Services.Workflows;
using KyoshinEewViewer.Services.Workflows.BuiltinActions;
using KyoshinMonitorLib;
using KyoshinEewViewer.Services.ExternalPublishers.Axis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;
using Microsoft.Extensions.Logging;

namespace KyoshinEewViewer.Series.KyoshinMonitor;

public partial class KyoshinMonitorSeries : SeriesBase
{
	public static SeriesMeta MetaData { get; } = new(typeof(KyoshinMonitorSeries), "kyoshin-monitor", "強震モニタ", new FAFontIconSource { Glyph = "\xe3b1", FontFamily = new(Utils.IconFontName) }, true, "強震モニタ･緊急地震速報を表示します。");

	public override Size MinViewSize { get; } = new(500, 600);

	public SoundCategory SoundCategory { get; } = new("KyoshinMonitor", "強震モニタ");
	private Sound WeakShakeDetectedSound { get; set; }
	private Sound MediumShakeDetectedSound { get; set; }
	private Sound StrongShakeDetectedSound { get; set; }
	private Sound StrongerShakeDetectedSound { get; set; }

	private ILogger Logger { get; }
	private WorkflowService WorkflowService { get; }
	private KyoshinEewViewerConfiguration Config { get; }

	private ShakeDetectionAreaLayer ShakeDetectionAreaLayer { get; set; }
	private KyoshinMonitorLayer KyoshinMonitorLayer { get; set; }

	private KyoshinMonitorView? _control;
	public override Control DisplayControl => _control ?? throw new InvalidOperationException("初期化前にコントロールが呼ばれています");

	private KyoshinMonitorReplaySettingPage ReplaySettingPage { get; }
	public override ISettingPage[] SettingPages => [
		new BasicSettingPage<KyoshinMonitorPage>("\xf108", "強震モニタ", [
			ReplaySettingPage,
			new BasicSettingPage<KyoshinMonitorMapPage>(null, "地図アイコン", []),
			new BasicSettingPage<KyoshinMonitorEewPage>(null, "緊急地震速報", []),
			new BasicSettingPage<ObservationPointsDataPage>(null, "観測点データ", []),
		]),
	];

	public Services.Eew.EewPointForecastController PointForecastController { get; }

	private RealtimeEarthquakeInformationHost RealtimeInformationHost { get; }
	private TimeshiftEarthquakeInformationHost TimeshiftInformationHost { get; }
	public ReplayFileEarthquakeInformationHost ReplayFileInformationHost { get; }

	private IDisposable? MapNavigationSubscription { get; set; }
	private IDisposable? MapDisplayParameterSubscription { get; set; }

	private EarthquakeInformationHost? _currentInformationHost;
	public EarthquakeInformationHost CurrentInformationHost
	{
		get => _currentInformationHost ?? throw new InvalidOperationException("初期化前に CurrentInformationHost が呼ばれています");
		set {
			if (_currentInformationHost == value)
				return;

			if (_currentInformationHost != null)
			{
				_currentInformationHost.EewUpdated -= EewUpdated;
				_currentInformationHost.RealtimeDataUpdated -= RealtimeDataUpdated;
				_currentInformationHost.KyoshinEventUpdated -= KyoshinEventUpdated;
			}
			SetProperty(ref _currentInformationHost, value);

			value.EewUpdated += EewUpdated;
			value.RealtimeDataUpdated += RealtimeDataUpdated;
			value.KyoshinEventUpdated += KyoshinEventUpdated;
			if (KyoshinMonitorLayer != null)
			{
				EewUpdated(DateTime.MinValue, value.Eews);
				KyoshinMonitorLayer.ObservationPoints = [];
				KyoshinMonitorLayer.KyoshinEvents = value.KyoshinEvents;
				ShakeDetectionAreaLayer.KyoshinEvents = value.KyoshinEvents;
			}

			MapNavigationSubscription?.Dispose();
			MapNavigationSubscription = value.ObservePropertyChanged(x => x.MapNavigationRequest).Subscribe(x =>
			{
				MapNavigationRequest = x;
				UpdatePadding();
			});

			MapDisplayParameterSubscription?.Dispose();
			MapDisplayParameterSubscription = value.ObservePropertyChanged(x => x.MapDisplayParameter).Subscribe(x => MapDisplayParameter = x with { OverlayLayers = [ShakeDetectionAreaLayer!, KyoshinMonitorLayer!], Padding = MapDisplayParameter.Padding });

			NowReplaying = value.IsReplay;
		}
	}

	[ObservableProperty]
	public partial bool NowReplaying { get; set; }

	public void StartTimeshift()
	{
		if (!Config.KyoshinMonitor.KeepReceiveDuringReplay)
			RealtimeInformationHost.Stop();
		TimeshiftInformationHost.Start(ReplaySettingPage.TimeshiftSeconds);
		CurrentInformationHost = TimeshiftInformationHost;
	}

	public void StartReplayFile()
		=> BeginReplayFileHost();

	/// <summary>
	/// EQMonitor API から取得した EEW 履歴を既存リプレイ基盤で再生する。
	/// リアルタイム停止設定・ホスト切替・終了/停止はファイルリプレイと共通。
	/// </summary>
	public void StartEewHistoryReplay(IReadOnlyList<Generated.EewItemWithRelations> items)
	{
		ArgumentNullException.ThrowIfNull(items);
		if (items.Count == 0)
			return;

		ReplayFileInformationHost.LoadFromEqMonitorEewItems(items);
		BeginReplayFileHost();
		ActiveRequest.Send(this);
	}

	/// <summary>
	/// 変換済み EEW の時系列を既存リプレイ基盤で再生する。
	/// </summary>
	public void StartEewHistoryReplay(IEnumerable<(DateTime Time, Eew Eew)> entries)
	{
		ArgumentNullException.ThrowIfNull(entries);

		ReplayFileInformationHost.LoadFromEews(entries);
		if (ReplayFileInformationHost.LoadedData is not { Length: > 0 })
			return;

		BeginReplayFileHost();
		ActiveRequest.Send(this);
	}

	/// <summary>
	/// リプレイファイルホストでの再生開始に共通する前処理とホスト切替
	/// </summary>
	private void BeginReplayFileHost()
	{
		if (!Config.KyoshinMonitor.KeepReceiveDuringReplay)
			RealtimeInformationHost.Stop();
		ReplayFileInformationHost.Start();
		CurrentInformationHost = ReplayFileInformationHost;
	}

	public void ReturnToRealtime()
	{
		if (CurrentInformationHost == RealtimeInformationHost)
			return;
		TimeshiftInformationHost.Stop();
		ReplayFileInformationHost.StopAsync().ConfigureAwait(false);
		RealtimeInformationHost.Start();
		CurrentInformationHost = RealtimeInformationHost;
	}

	public DateTime CurrentDisplayTime => _currentInformationHost?.CurrentTime ?? DateTime.Now;

	private Avalonia.Rect _widgetRect;
	public Avalonia.Rect WidgetRect
	{
		get => _widgetRect;
		set {
			SetProperty(ref _widgetRect, value);
			UpdatePadding();
		}
	}

	private Avalonia.Rect _viewRect;
	public Avalonia.Rect ViewRect
	{
		get => _viewRect;
		set {
			SetProperty(ref _viewRect, value);
			UpdatePadding();
		}
	}

	public KyoshinMonitorSeries(
		ILogger<KyoshinMonitorSeries> logger,
		KyoshinEewViewerConfiguration config,
		NotificationService notificationService,
		SoundPlayerService soundPlayer,
		WorkflowService workflowService,
		TimerService timerService,
		TelegramProvideService telegramProvideService,
		AxisInformationProvider axis,
		Services.ObservationPointsUpdateService observationPointsUpdateService,
		EqMonitorApiProvider eqMonitorApiProvider,
		ISubWindowsService? subWindowService = null) : base(MetaData)
	{
		Logger = logger;
		Config = config;
		WorkflowService = workflowService;

		WeakShakeDetectedSound = soundPlayer.RegisterSound(SoundCategory, "WeakShakeDetected", "揺れ検出(震度1未満)", "鳴動させるためには揺れ検出の設定を有効にしている必要があります。\n{mode}: 再生モード [replay, realtime]", new() { { "mode", "realtime" }, });
		MediumShakeDetectedSound = soundPlayer.RegisterSound(SoundCategory, "MediumShakeDetected", "揺れ検出(震度1以上3未満)", "震度上昇時にも鳴動します。\n鳴動させるためには揺れ検出の設定を有効にしている必要があります。\n{mode}: 再生モード [replay, realtime]", new() { { "mode", "realtime" }, });
		StrongShakeDetectedSound = soundPlayer.RegisterSound(SoundCategory, "StrongShakeDetected", "揺れ検出(震度3以上5弱未満)", "震度上昇時にも鳴動します。\n鳴動させるためには揺れ検出の設定を有効にしている必要があります。\n{mode}: 再生モード [replay, realtime]", new() { { "mode", "replay" }, });
		StrongerShakeDetectedSound = soundPlayer.RegisterSound(SoundCategory, "StrongerShakeDetected", "揺れ検出(震度5弱以上)", "震度上昇時にも鳴動します。\n鳴動させるためには揺れ検出の設定を有効にしている必要があります。\n{mode}: 再生モード [replay, realtime]", new() { { "mode", "replay" }, });

		ReplaySettingPage = new KyoshinMonitorReplaySettingPage(Config, this, timerService, subWindowService);

		var eewController = new Services.Eew.EewController(AppLog.Create<Services.Eew.EewController>(), this, config, soundPlayer, workflowService);
		PointForecastController = new(AppLog.Create<Services.Eew.EewPointForecastController>(), config, eewController, timerService);
		CurrentInformationHost = RealtimeInformationHost = new(AppLog.Create<RealtimeEarthquakeInformationHost>(), config, eewController, timerService, telegramProvideService, axis, observationPointsUpdateService, eqMonitorApiProvider);
		RegisterSystemWorkflows();
		RealtimeInformationHost.KyoshinEventUpdated += e =>
		{
			if (Config.KyoshinMonitor.ReturnToRealtimeAtShakeDetected && e.isLevelUp)
				ReturnToRealtime();
		};
		RealtimeInformationHost.EewUpdated += (t, e) =>
		{
			if (Config.KyoshinMonitor.ReturnToRealtimeAtEewReceived && e.Length > 0)
				ReturnToRealtime();
		};
		TimeshiftInformationHost = new(AppLog.Create<TimeshiftEarthquakeInformationHost>(), this, config, timerService, notificationService, soundPlayer, workflowService, observationPointsUpdateService);
		ReplayFileInformationHost = new(AppLog.Create<ReplayFileEarthquakeInformationHost>(), this, config, notificationService, soundPlayer, workflowService, timerService, observationPointsUpdateService);

		ShakeDetectionAreaLayer = new(config, this);
		KyoshinMonitorLayer = new(config, this);
		// 地点予測の残り秒数の更新を地図へ反映する
		PointForecastController.DisplayValuesUpdated += () => KyoshinMonitorLayer.RefreshPointForecast();
		TimeshiftInformationHost.PointForecastController.DisplayValuesUpdated += () => KyoshinMonitorLayer.RefreshPointForecast();
		ReplayFileInformationHost.PointForecastController.DisplayValuesUpdated += () => KyoshinMonitorLayer.RefreshPointForecast();
		MapDisplayParameter = new() { OverlayLayers = [ShakeDetectionAreaLayer, KyoshinMonitorLayer] };

		config.Eew.ObservePropertyChanged(x => x.ShowDetails).Subscribe(x => ShowEewAccuracy = x);
		config.KyoshinMonitor.ObservePropertyChanged(x => x.ShowColorSample).Subscribe(x => ShowColorSample = x);
	}
	public override void Initialize()
	{
		StrongReferenceMessenger.Default.Register<MapLoaded>(this, (_, x) => RealtimeInformationHost.MapData = TimeshiftInformationHost.MapData = ReplayFileInformationHost.MapData = x.Data);
		RealtimeInformationHost.Start();

		// 全 Series の初期化完了後に、有効な EEW履歴 Series へリプレイ要求を接続する
		Avalonia.Threading.Dispatcher.UIThread.Post(ConnectEewHistoryReplay);
	}

	/// <summary>
	/// 有効化されている EEW履歴 Series のリプレイ要求を購読する
	/// </summary>
	private void ConnectEewHistoryReplay()
	{
		var eewHistory = ServiceLocator.Current.RequireService<SeriesController>().EnabledSeries
			.OfType<EewHistorySeries>()
			.FirstOrDefault();
		if (eewHistory == null)
			return;

		eewHistory.ReplayRequested += HandleEewHistoryReplayRequested;
		Logger.LogDebug("EEW履歴Seriesのリプレイ要求を購読しました");
	}

	/// <summary>
	/// EEW履歴Seriesからのリプレイ要求を処理する
	/// </summary>
	private Task HandleEewHistoryReplayRequested(EewHistoryReplayRequest request)
	{
		if (request.RawItems.Count == 0)
		{
			Logger.LogWarning($"EEW履歴リプレイを開始できませんでした: 各報がありません ({request.EventId})");
			return Task.CompletedTask;
		}

		Logger.LogInformation("EEW履歴からのリプレイを開始します: {EventId}（{ReportCount}報）", request.EventId, request.RawItems.Count);
		// Generated DTO をそのまま渡し、ホスト側で ToEew 変換する（JSON 再シリアライズしない）
		StartEewHistoryReplay(request.RawItems);
		return Task.CompletedTask;
	}

	public override void RecreateDisplayControl()
		=> _control = new KyoshinMonitorView { DataContext = this };

	public void EewUpdated(DateTime updatedTime, Eew[] eews)
	{
		KyoshinMonitorLayer.CurrentEews = eews.OrderByDescending(eew => eew.Hypocenter?.OccurrenceTime).ToArray();
	}

	public void RealtimeDataUpdated((DateTime time, RealtimeObservationPoint[] data, KyoshinEvent[] events) e)
	{
		KyoshinMonitorLayer.ObservationPoints = e.data;
		KyoshinMonitorLayer.KyoshinEvents = e.events;
		ShakeDetectionAreaLayer.KyoshinEvents = e.events;
	}

	public void KyoshinEventUpdated((DateTime time, KyoshinEvent e, bool isLevelUp, bool isRegionExpanded, bool isSubRegionExpanded) e)
	{
		var regionDetails = ShakeDetectedRegionBuilder.Build([e.e], CurrentInformationHost.RegionSubRegionMap);
		WorkflowService.PublishEvent(new ShakeDetectedEvent(this, e.time, e.e, NowReplaying, e.isRegionExpanded, e.isSubRegionExpanded, regionDetails));

		// 音声再生は地域拡大時には行わない（初回検知・レベル上昇時のみ）
		if (e.isRegionExpanded || e.isSubRegionExpanded)
			return;

		switch (e.e.Level)
		{
			case KyoshinEventLevel.Weak:
				WeakShakeDetectedSound.Play(new() { { "mode", NowReplaying ? "replay" : "realtime" } });
				break;
			case KyoshinEventLevel.Medium:
				MediumShakeDetectedSound.Play(new() { { "mode", NowReplaying ? "replay" : "realtime" } });
				break;
			case KyoshinEventLevel.Strong:
				StrongShakeDetectedSound.Play(new() { { "mode", NowReplaying ? "replay" : "realtime" } });
				break;
			case KyoshinEventLevel.Stronger:
				StrongerShakeDetectedSound.Play(new() { { "mode", NowReplaying ? "replay" : "realtime" } });
				break;
		}
		StrongReferenceMessenger.Default.Send(new KyoshinShakeDetected(e.e, e.isLevelUp, NowReplaying));
	}

	private void UpdatePadding()
	{
		if (MapNavigationRequest == null)
		{
			if (MapDisplayParameter.Padding != default)
				MapDisplayParameter = MapDisplayParameter with { Padding = default };
			return;
		}
		var widthArea = (ViewRect.Width - WidgetRect.Width) * ViewRect.Height;
		var heightArea = (ViewRect.Height - WidgetRect.Height) * ViewRect.Width;

		var padding = widthArea > heightArea ?
			new Avalonia.Thickness(WidgetRect.Width, 0, 0, 0) :
			new Avalonia.Thickness(0, WidgetRect.Height, 0, 0);

		if (MapDisplayParameter.Padding != padding)
		{
			MapDisplayParameter = MapDisplayParameter with { Padding = padding };
			MapNavigationRequest = new(MapNavigationRequest.Bound, null);
		}
	}

	public bool IsDebug { get; }
#if DEBUG
		= true;
#endif

	[ObservableProperty]
	public partial bool ShowColorSample { get; set; }

	[ObservableProperty]
	public partial bool ShowEewAccuracy { get; set; } = false;


	private void RegisterSystemWorkflows()
	{
		// EEW受信通知のSystemWorkflow
		var eewReceivedWorkflow = new WorkflowsNamespace.Workflow
		{
			Name = "System: EEW受信通知",
			Trigger = new EewTrigger
			{
				New = true,
				Continue = true,
				UpdateWithMoreAccurate = false,
				Final = true,
				Cancel = true,
				NewWarning = false,
				ContinueWarning = false,
				CancelWarning = false,
				WarningLevelReached = false,
				IncreaseInIntensity = false,
				DecreaseInIntensity = false,
				Intensity = JmaIntensity.Unknown
			},
			Actions = new MultipleAction
			{
				ChildActions =
				{
					new ChildAction
					{
						Action = new SendNotificationAction
						{
							Title = KyoshinMonitorNotificationTemplates.EewNotificationTitle,
							TemplateText = KyoshinMonitorNotificationTemplates.EewNotificationMessage,
							// 震度5弱以上は重要 (おやすみ中も表示)
							Urgency = "{{ if IsAtLeastInt5Lower }}critical{{ else }}normal{{ end }}",
						}
					}
				}
			}
		};

		// 設定変更監視でEnabled状態を制御
		Config.ObservePropertyChanged(x => x.Notification, x => x.EewReceived)
			.Subscribe(enabled => eewReceivedWorkflow.Enabled = enabled);

		WorkflowService.SystemWorkflows.Add(eewReceivedWorkflow);

		// EEW受信時のタブ切り替えSystemWorkflow
		var eewSwitchWorkflow = new WorkflowsNamespace.Workflow
		{
			Name = "System: EEW受信時タブ切り替え",
			Trigger = new EewTrigger
			{
				New = true,
				Continue = true,
				UpdateWithMoreAccurate = false,
				Final = false,
				Cancel = false,
				NewWarning = false,
				ContinueWarning = false,
				CancelWarning = false,
				WarningLevelReached = false,
				IncreaseInIntensity = false,
				DecreaseInIntensity = false,
				Intensity = JmaIntensity.Unknown
			},
			Actions = new MultipleAction
			{
				ChildActions =
				{
					new ChildAction { Action = new SwitchTabAction() }
				}
			}
		};

		Config.ObservePropertyChanged(x => x.Eew, x => x.SwitchAtAnnounce)
			.Subscribe(enabled => eewSwitchWorkflow.Enabled = enabled);

		WorkflowService.SystemWorkflows.Add(eewSwitchWorkflow);

		// 揺れ検出時のタブ切り替えSystemWorkflow
		var shakeSwitchWorkflow = new WorkflowsNamespace.Workflow
		{
			Name = "System: 揺れ検出時タブ切り替え",
			Trigger = new ShakeDetectTrigger
			{
				Level = Config.KyoshinMonitor.EventNotificationLevel
			},
			Actions = new MultipleAction
			{
				ChildActions =
				{
					new ChildAction { Action = new SwitchTabAction() }
				}
			}
		};

		Config.ObservePropertyChanged(x => x.KyoshinMonitor, x => x.SwitchAtShakeDetect)
			.Subscribe(enabled => shakeSwitchWorkflow.Enabled = enabled);

		WorkflowService.SystemWorkflows.Add(shakeSwitchWorkflow);
	}
}
