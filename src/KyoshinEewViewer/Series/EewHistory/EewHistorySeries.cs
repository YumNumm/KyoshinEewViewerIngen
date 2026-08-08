using Avalonia;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using KyoshinEewViewer.Core;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Series.EewHistory.Models;
using KyoshinEewViewer.Series.KyoshinMonitor.Models;
using KyoshinEewViewer.Services.EqMonitor;
using ReactiveUI;
using Splat;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Series.EewHistory;

public class EewHistorySeries : SeriesBase
{
	public static SeriesMeta MetaData { get; } = new(
		typeof(EewHistorySeries),
		"eew-history",
		"緊急地震速報履歴",
		new FAFontIconSource { Glyph = "\xf1da", FontFamily = new(Utils.IconFontName) },
		true,
		"緊急地震速報の過去電文を閲覧し、リプレイを開始できます。"
	);

	private ILogger Logger { get; }
	private KyoshinEewViewerConfiguration Config { get; }

	/// <summary>
	/// EQMonitor API からの EEW 履歴取得を担う
	/// </summary>
	public EqMonitorEewHistoryService Service { get; }

	/// <summary>
	/// 検索条件の入力内容。ダイアログを開き直しても保つ
	/// </summary>
	public EewHistorySearchModel SearchModel { get; } = new();

	private EewHistoryView? _control;
	public override Control DisplayControl => _control ?? throw new InvalidOperationException("初期化前にコントロールが呼ばれています");
	public override ISettingPage[] SettingPages => [];

	/// <summary>
	/// 狭い画面でも情報カードが潰れないようスマートフォン相当の最小サイズに留める
	/// </summary>
	public override Size MinViewSize { get; } = new(380, 400);

	/// <summary>
	/// リプレイ開始が要求されたときに発火する
	/// </summary>
	/// <remarks>
	/// 既存リプレイホストへの接続は購読側が実装する
	/// </remarks>
	public event Func<EewHistoryReplayRequest, Task>? ReplayRequested;

	private bool _suppressSelection;

	public EewHistorySeries(
		ILogManager logManager,
		KyoshinEewViewerConfiguration config,
		EqMonitorEewHistoryService service) : base(MetaData)
	{
		SplatRegistrations.RegisterLazySingleton<EewHistorySeries>();

		Logger = logManager.GetLogger<EewHistorySeries>();
		Config = config;
		Service = service;

		if (Design.IsDesignMode)
		{
			SetupDesignData();
			return;
		}

		Config.EqMonitor.WhenAnyValue(x => x.Enable, x => x.EnableEew, x => x.BaseUrl)
			.ObserveOn(RxSchedulers.MainThreadScheduler)
			.Subscribe(_ =>
			{
				this.RaisePropertyChanged(nameof(CanSearch));
				if (!CanSearch)
					ClearSearch();
			});

		Service.WhenAnyValue(
				x => x.IsLoading,
				x => x.IsLoadingMore,
				x => x.IsLoadingReports,
				x => x.CanLoadMore,
				x => x.ReportsError,
				x => x.SelectedItem)
			.ObserveOn(RxSchedulers.MainThreadScheduler)
			.Subscribe(_ =>
			{
				this.RaisePropertyChanged(nameof(IsLoading));
				this.RaisePropertyChanged(nameof(IsLoadingMore));
				this.RaisePropertyChanged(nameof(IsLoadingReports));
				this.RaisePropertyChanged(nameof(CanLoadMoreHistory));
				this.RaisePropertyChanged(nameof(ReportsError));
				this.RaisePropertyChanged(nameof(CurrentEvent));
				this.RaisePropertyChanged(nameof(CanStartReplay));
			});

		Service.SelectedReports.CollectionChanged += (_, _) =>
		{
			this.RaisePropertyChanged(nameof(CanStartReplay));
			// 選択イベントの各報が入れ替わったら最終報（または末尾）を選ぶ
			if (_suppressSelection)
				return;
			SelectedReport = Service.SelectedReports.LastOrDefault(r => r.IsFinal)
				?? Service.SelectedReports.LastOrDefault();
		};
	}

	public override void Initialize()
	{
		_ = RefreshAsync();
	}

	public override void RecreateDisplayControl()
	{
		_control = new EewHistoryView { DataContext = this };
	}

	/// <summary>
	/// 右側に表示するイベント履歴一覧
	/// </summary>
	public ObservableCollection<EewHistoryListItem> DisplayedEvents => Service.Items;

	/// <summary>
	/// 左側に表示する、選択イベントの各報一覧
	/// </summary>
	public ObservableCollection<EewHistoryListItem> CurrentReports => Service.SelectedReports;

	/// <summary>
	/// 選択中のEEWイベント（最終報相当）
	/// </summary>
	public EewHistoryListItem? CurrentEvent
	{
		get => Service.SelectedItem;
		set {
			if (ReferenceEquals(Service.SelectedItem, value))
				return;
			if (value == null)
			{
				Service.ClearSelection();
				SelectedReport = null;
				return;
			}
			_ = SelectEventAsync(value);
		}
	}

	private EewHistoryListItem? _selectedReport;
	/// <summary>
	/// 選択中の報
	/// </summary>
	public EewHistoryListItem? SelectedReport
	{
		get => _selectedReport;
		set {
			if (ReferenceEquals(_selectedReport, value))
				return;
			if (_selectedReport != null && !ReferenceEquals(_selectedReport, Service.SelectedItem))
				_selectedReport.IsSelecting = false;
			this.RaiseAndSetIfChanged(ref _selectedReport, value);
			if (_selectedReport != null)
				_selectedReport.IsSelecting = true;
			SelectedEew = _selectedReport?.Eew;
		}
	}

	private Eew? _selectedEew;
	/// <summary>
	/// 中央の詳細パネルへ渡すEEW
	/// </summary>
	public Eew? SelectedEew
	{
		get => _selectedEew;
		private set => this.RaiseAndSetIfChanged(ref _selectedEew, value);
	}

	/// <summary>
	/// EQMonitor API で検索できる状態か
	/// </summary>
	public bool CanSearch => Config.EqMonitor is { Enable: true, EnableEew: true }
		&& !string.IsNullOrWhiteSpace(Config.EqMonitor.BaseUrl);

	public bool IsLoading => Service.IsLoading;
	public bool IsLoadingMore => Service.IsLoadingMore;
	public bool IsLoadingReports => Service.IsLoadingReports;
	public string? ReportsError => Service.ReportsError;

	private bool _isSearching;
	/// <summary>
	/// 検索結果を表示しているか
	/// </summary>
	public bool IsSearching
	{
		get => _isSearching;
		private set => this.RaiseAndSetIfChanged(ref _isSearching, value);
	}

	private bool _isFault;
	/// <summary>
	/// 一覧取得に失敗しているか
	/// </summary>
	public bool IsFault
	{
		get => _isFault;
		private set => this.RaiseAndSetIfChanged(ref _isFault, value);
	}

	private string? _loadError;
	/// <summary>
	/// 一覧取得の失敗理由
	/// </summary>
	public string? LoadError
	{
		get => _loadError;
		private set => this.RaiseAndSetIfChanged(ref _loadError, value);
	}

	/// <summary>
	/// 一覧末尾でさらに読み込めるか
	/// </summary>
	public bool CanLoadMoreHistory => Service.CanLoadMore;

	/// <summary>
	/// リプレイを開始できるか
	/// </summary>
	public bool CanStartReplay => CurrentEvent != null && CurrentReports.Count > 0;

	/// <summary>
	/// 履歴を再取得する
	/// </summary>
	public async Task RefreshAsync()
	{
		if (!CanSearch)
		{
			IsFault = false;
			LoadError = null;
			Service.Clear();
			SelectedReport = null;
			return;
		}

		var error = await Service.LoadAsync();
		IsFault = error != null;
		LoadError = error;
		IsSearching = false;
		if (error == null && DisplayedEvents.Count > 0)
			await SelectEventAsync(DisplayedEvents[0]);
	}

	/// <summary>
	/// 一覧の末尾までスクロールされた際に、さらに過去の履歴を読み込む
	/// </summary>
	public async Task LoadMoreHistoryAsync()
	{
		try
		{
			await Service.LoadMoreAsync();
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "過去のEEW履歴の読み込み中に例外が発生しました");
		}
		finally
		{
			this.RaisePropertyChanged(nameof(CanLoadMoreHistory));
		}
	}

	/// <summary>
	/// 条件を指定してEEW履歴を検索する
	/// </summary>
	public async Task<string?> SearchAsync(EqMonitorEewSearchCondition condition)
	{
		if (!CanSearch)
			return "EQMonitor API が有効になっていません。設定から接続先を確認してください。";

		SelectedReport = null;
		var error = await Service.SearchAsync(condition);
		if (error != null)
		{
			IsFault = true;
			LoadError = error;
			return error;
		}

		IsFault = false;
		LoadError = null;
		IsSearching = true;
		if (DisplayedEvents.Count > 0)
			await SelectEventAsync(DisplayedEvents[0]);
		return null;
	}

	/// <summary>
	/// 検索を解除して通常の一覧へ戻す
	/// </summary>
	public void ClearSearch()
	{
		if (!IsSearching)
			return;

		IsSearching = false;
		_ = RefreshAsync();
	}

	/// <summary>
	/// 検索ダイアログを開く
	/// </summary>
	public async Task OpenSearchAsync()
	{
		if (!CanSearch)
			return;

		SearchModel.Error = null;
		while (true)
		{
			var view = new EewHistorySearchView { DataContext = SearchModel };
			var dialog = new FAContentDialog
			{
				Title = "緊急地震速報履歴の検索",
				Content = view,
				PrimaryButtonText = "検索",
				SecondaryButtonText = "条件をクリア",
				CloseButtonText = "閉じる",
				DefaultButton = FAContentDialogButton.Primary,
			};

			var result = await dialog.ShowAsync();
			if (result == FAContentDialogResult.Secondary)
			{
				SearchModel.Reset();
				continue;
			}
			if (result != FAContentDialogResult.Primary)
				return;

			SearchModel.IsSearching = true;
			try
			{
				SearchModel.Error = await SearchAsync(SearchModel.ToCondition());
			}
			finally
			{
				SearchModel.IsSearching = false;
			}

			if (SearchModel.Error == null)
				return;
		}
	}

	/// <summary>
	/// 選択中イベントのリプレイ開始を要求する
	/// </summary>
	/// <remarks>
	/// リプレイホストへの接続は <see cref="ReplayRequested"/> の購読側が行う
	/// </remarks>
	public async Task StartReplayAsync()
	{
		if (CurrentEvent is not { } evt)
		{
			Logger.LogWarning("リプレイ開始が要求されましたが、イベントが選択されていません");
			return;
		}
		if (CurrentReports.Count == 0)
		{
			Logger.LogWarning($"リプレイ開始が要求されましたが、イベント {evt.EventId} に各報がありません");
			return;
		}

		var request = new EewHistoryReplayRequest(evt.EventId, CurrentReports.ToArray(), evt);
		Logger.LogInfo($"EEW履歴からのリプレイ開始を要求します: {evt.EventId}（{CurrentReports.Count}報）");

		if (ReplayRequested is null)
		{
			Logger.LogWarning("リプレイ開始の接続先が未登録です。購読側の実装を待っています");
			if (KyoshinEewViewerApp.TopLevelControl is Window window)
			{
				await new FAContentDialog
				{
					Title = "リプレイ",
					Content = "リプレイ機能への接続はまだ用意されていません。",
					CloseButtonText = "OK",
				}.ShowAsync(window);
			}
			return;
		}

		try
		{
			await ReplayRequested.Invoke(request);
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "リプレイ開始の通知中に例外が発生しました");
		}
	}

	private async Task SelectEventAsync(EewHistoryListItem item)
	{
		_suppressSelection = true;
		try
		{
			SelectedReport = null;
			await Service.SelectAsync(item);
			this.RaisePropertyChanged(nameof(CurrentEvent));
			SelectedReport = Service.SelectedReports.LastOrDefault(r => r.IsFinal)
				?? Service.SelectedReports.LastOrDefault();
		}
		finally
		{
			_suppressSelection = false;
			this.RaisePropertyChanged(nameof(CanStartReplay));
		}
	}

	#region レスポンシブ

	/// <summary>
	/// 各報一覧・イベント履歴をシートへ移す画面幅の上限
	/// </summary>
	private const double NarrowLayoutMaxWidth = 900;

	private double _viewWidth = double.PositiveInfinity;
	/// <summary>
	/// ビューの表示幅
	/// </summary>
	public double ViewWidth
	{
		get => _viewWidth;
		set {
			if (_viewWidth == value)
				return;
			this.RaiseAndSetIfChanged(ref _viewWidth, value);
			IsNarrowLayout = value < NarrowLayoutMaxWidth;
		}
	}

	private bool _isNarrowLayout;
	/// <summary>
	/// 狭い画面向けレイアウトか
	/// </summary>
	public bool IsNarrowLayout
	{
		get => _isNarrowLayout;
		private set {
			if (_isNarrowLayout == value)
				return;
			this.RaiseAndSetIfChanged(ref _isNarrowLayout, value);
			if (!value)
				CloseSheets();
		}
	}

	private bool _isReportsSheetOpen;
	/// <summary>
	/// 各報一覧シートを表示しているか
	/// </summary>
	public bool IsReportsSheetOpen
	{
		get => _isReportsSheetOpen;
		set {
			if (_isReportsSheetOpen == value)
				return;
			this.RaiseAndSetIfChanged(ref _isReportsSheetOpen, value);
			if (value)
				IsHistorySheetOpen = false;
		}
	}

	private bool _isHistorySheetOpen;
	/// <summary>
	/// イベント履歴シートを表示しているか
	/// </summary>
	public bool IsHistorySheetOpen
	{
		get => _isHistorySheetOpen;
		set {
			if (_isHistorySheetOpen == value)
				return;
			this.RaiseAndSetIfChanged(ref _isHistorySheetOpen, value);
			if (value)
				IsReportsSheetOpen = false;
		}
	}

	public void CloseSheets()
	{
		IsReportsSheetOpen = false;
		IsHistorySheetOpen = false;
	}

	#endregion

	#region デザイン用データ

	private void SetupDesignData()
	{
		var first = CreateDesignItem("20240101120000", 1, false, false, false);
		var last = CreateDesignItem("20240101120000", 12, true, true, false);
		Service.Items.Add(last);
		Service.SelectedReports.Add(first);
		Service.SelectedReports.Add(last);
		// デザイン時は API を叩かず、詳細表示用の報だけ選ぶ
		SelectedReport = last;
	}

	private static EewHistoryListItem CreateDesignItem(
		string eventId, int serialNo, bool isWarning, bool isFinal, bool isPlum)
	{
		var time = new DateTimeOffset(DateTime.Today.AddHours(12).AddMinutes(serialNo), TimeSpan.FromHours(9));
		return new EewHistoryListItem(new Generated.EewItemWithRelations
		{
			Event_id = eventId,
			Type = Generated.TelegramType.VXSE45,
			Status = Generated.TelegramStatus.NORMAL,
			Info_type = Generated.EewItemWithRelationsInfo_type.PUBLICATION,
			Serial_no = serialNo,
			Headline = null,
			Is_canceled = false,
			Is_warning = isWarning,
			Is_last_info = isFinal,
			Origin_time = time,
			Arrival_time = time,
			Hypocenter = new Generated.EewHypocenter
			{
				Code = "300",
				Name = "駿河湾",
				Magnitude = 6.2,
				Depth = 10,
			},
			Forecast_intensity = new Generated.EewIntensity
			{
				Max_intensity = new Generated.EewIntensityValue
				{
					Value = Generated.JmaIntensity._5Minus,
					Is_over = false,
				},
				Regions = [],
			},
			Accuracy = null,
			Is_plum = isPlum,
			Editorial_office = "気象庁",
			Report_time = time,
			Warning = null,
		});
	}

	#endregion
}
