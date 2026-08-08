using KyoshinEewViewer.Core;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Series.EewHistory.Models;
using ReactiveUI;
using Splat;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Services.EqMonitor;

/// <summary>
/// EQMonitor API で緊急地震速報の履歴を取得する
/// </summary>
/// <remarks>
/// GET /v2/eew は最終報の一覧を返す。詳細はイベントID単位で
/// GET /v2/eew/{eventId} から全報を取得する
/// </remarks>
public class EqMonitorEewHistoryService : ReactiveObject
{
	private ILogger Logger { get; }
	private KyoshinEewViewerConfiguration Config { get; }
	private EqMonitorApiProvider ApiProvider { get; }

	/// <summary>
	/// 最終報の一覧（API 応答をラップしたもの）
	/// </summary>
	public ObservableCollection<EewHistoryListItem> Items { get; } = [];

	/// <summary>
	/// 選択中イベントの全報（serial_no / report_time 順）
	/// </summary>
	public ObservableCollection<EewHistoryListItem> SelectedReports { get; } = [];

	private readonly SemaphoreSlim _lock = new(1, 1);
	private string? _nextCursor;
	private EqMonitorEewSearchCondition? _condition;
	private bool _isLoadMoreFailing;
	private CancellationTokenSource? _selectCts;

	private bool _canLoadMore;
	/// <summary>
	/// さらに過去の履歴を読み込めるか
	/// </summary>
	public bool CanLoadMore
	{
		get => _canLoadMore;
		private set => this.RaiseAndSetIfChanged(ref _canLoadMore, value);
	}

	private bool _isLoadingMore;
	/// <summary>
	/// 追加の読み込み中か
	/// </summary>
	public bool IsLoadingMore
	{
		get => _isLoadingMore;
		private set => this.RaiseAndSetIfChanged(ref _isLoadingMore, value);
	}

	private bool _isLoading;
	/// <summary>
	/// 一覧の読み込み中か
	/// </summary>
	public bool IsLoading
	{
		get => _isLoading;
		private set => this.RaiseAndSetIfChanged(ref _isLoading, value);
	}

	private bool _isLoadingReports;
	/// <summary>
	/// 選択イベントの全報取得中か
	/// </summary>
	public bool IsLoadingReports
	{
		get => _isLoadingReports;
		private set => this.RaiseAndSetIfChanged(ref _isLoadingReports, value);
	}

	private EewHistoryListItem? _selectedItem;
	/// <summary>
	/// 選択中の一覧行
	/// </summary>
	public EewHistoryListItem? SelectedItem
	{
		get => _selectedItem;
		private set => this.RaiseAndSetIfChanged(ref _selectedItem, value);
	}

	private string? _reportsError;
	/// <summary>
	/// 全報取得が失敗した理由
	/// </summary>
	public string? ReportsError
	{
		get => _reportsError;
		private set => this.RaiseAndSetIfChanged(ref _reportsError, value);
	}

	public EqMonitorEewHistoryService(
		ILogManager logManager,
		KyoshinEewViewerConfiguration config,
		EqMonitorApiProvider apiProvider)
	{
		SplatRegistrations.RegisterLazySingleton<EqMonitorEewHistoryService>();

		Logger = logManager.GetLogger<EqMonitorEewHistoryService>();
		Config = config;
		ApiProvider = apiProvider;
	}

	/// <summary>
	/// 条件を指定して履歴を読み込む
	/// </summary>
	/// <returns>読み込めなかった場合はその理由。成功した場合は null</returns>
	public async Task<string?> LoadAsync(EqMonitorEewSearchCondition? condition = null)
	{
		if (ApiProvider.GetClient() is not { } client)
			return "EQMonitor API の接続先が設定されていません。";

		var normalized = (condition ?? new EqMonitorEewSearchCondition()).Normalize();

		IsLoading = true;
		await _lock.WaitAsync();
		try
		{
			var response = await RequestListAsync(client, normalized, cursor: null);

			ClearSelection();
			Items.Clear();
			AppendItems(response.Items);

			_condition = normalized;
			_nextCursor = response.Next_token;
			_isLoadMoreFailing = false;
			CanLoadMore = _nextCursor != null;

			Logger.LogInfo($"EQMonitor API で EEW 履歴を取得し {Items.Count} 件が該当しました");
			return null;
		}
		catch (Exception ex)
		{
			Logger.LogWarning($"EQMonitor API での EEW 履歴の取得に失敗しました: {ex.Message}");
			return $"取得に失敗しました: {ex.Message}";
		}
		finally
		{
			_lock.Release();
			IsLoading = false;
		}
	}

	/// <summary>
	/// 検索条件を指定して履歴を読み込む
	/// </summary>
	/// <returns>検索できなかった場合はその理由。成功した場合は null</returns>
	public Task<string?> SearchAsync(EqMonitorEewSearchCondition condition)
		=> LoadAsync(condition);

	/// <summary>
	/// 履歴の続きを読み込む
	/// </summary>
	public async Task LoadMoreAsync()
	{
		if (!CanLoadMore || IsLoadingMore)
			return;
		if (_condition is not { } condition || _nextCursor is not { } cursor)
			return;
		if (ApiProvider.GetClient() is not { } client)
			return;

		IsLoadingMore = true;
		await _lock.WaitAsync();
		try
		{
			var response = await RequestListAsync(client, condition, cursor);
			AppendItems(response.Items);

			_nextCursor = response.Next_token;
			// 空のページが返ってきた時点で終端とみなす
			CanLoadMore = _nextCursor != null && response.Items.Count > 0;
			_isLoadMoreFailing = false;
		}
		catch (Exception ex)
		{
			// 復帰するまでは最初の1回だけ記録する
			if (!_isLoadMoreFailing)
			{
				_isLoadMoreFailing = true;
				Logger.LogWarning($"EQMonitor API での EEW 履歴の追加読み込みに失敗しました: {ex.Message}");
			}
		}
		finally
		{
			_lock.Release();
			IsLoadingMore = false;
		}
	}

	/// <summary>
	/// 一覧行を選択し、当該イベントの全報を取得する
	/// </summary>
	/// <returns>取得できなかった場合はその理由。成功した場合は null</returns>
	public Task<string?> SelectAsync(EewHistoryListItem item)
		=> SelectAsync(item.EventId);

	/// <summary>
	/// イベントIDを指定して全報を取得する
	/// </summary>
	/// <returns>取得できなかった場合はその理由。成功した場合は null</returns>
	public async Task<string?> SelectAsync(string eventId)
	{
		if (string.IsNullOrWhiteSpace(eventId))
			return "イベントIDが指定されていません。";
		if (ApiProvider.GetClient() is not { } client)
			return "EQMonitor API の接続先が設定されていません。";

		_selectCts?.Cancel();
		_selectCts?.Dispose();
		_selectCts = new CancellationTokenSource();
		var token = _selectCts.Token;

		var listItem = Items.FirstOrDefault(i => i.EventId == eventId);
		SetSelectedItem(listItem);

		IsLoadingReports = true;
		ReportsError = null;
		SelectedReports.Clear();
		try
		{
			var response = await client.GetV2EewByEventIdAsync(eventId, token);
			token.ThrowIfCancellationRequested();

			foreach (var report in EewHistoryReportOrdering.ToOrderedListItems(response.Items))
				SelectedReports.Add(report);

			Logger.LogInfo($"EQMonitor API で EEW 全報を取得しました: {eventId} ({SelectedReports.Count} 報)");
			return null;
		}
		catch (OperationCanceledException)
		{
			return null;
		}
		catch (Exception ex)
		{
			Logger.LogWarning($"EQMonitor API での EEW 全報の取得に失敗しました: {ex.Message}");
			var message = $"全報の取得に失敗しました: {ex.Message}";
			ReportsError = message;
			return message;
		}
		finally
		{
			if (!token.IsCancellationRequested)
				IsLoadingReports = false;
		}
	}

	/// <summary>
	/// 一覧と選択状態を破棄する
	/// </summary>
	public void Clear()
	{
		ClearSelection();
		Items.Clear();
		_condition = null;
		_nextCursor = null;
		_isLoadMoreFailing = false;
		CanLoadMore = false;
	}

	/// <summary>
	/// 選択状態のみを破棄する
	/// </summary>
	public void ClearSelection()
	{
		_selectCts?.Cancel();
		_selectCts?.Dispose();
		_selectCts = null;

		SetSelectedItem(null);
		SelectedReports.Clear();
		ReportsError = null;
		IsLoadingReports = false;
	}

	private Task<Generated.EewListResponse> RequestListAsync(
		Generated.EqMonitorApiClient client, EqMonitorEewSearchCondition c, string? cursor)
		=> client.GetV2EewAsync(
			limit: Math.Clamp(Config.EqMonitor.EarthquakeFetchCount, 1, 100).ToString(CultureInfo.InvariantCulture),
			cursor: cursor,
			magnitudeLte: EqMonitorEewSearchCondition.ToQueryValue(c.MagnitudeTo),
			magnitudeGte: EqMonitorEewSearchCondition.ToQueryValue(c.MagnitudeFrom),
			depthLte: EqMonitorEewSearchCondition.ToQueryValue(c.DepthTo),
			depthGte: EqMonitorEewSearchCondition.ToQueryValue(c.DepthFrom),
			intensityLte: c.IntensityTo,
			intensityGte: c.IntensityFrom,
			originTimeGte: c.OriginTimeFrom,
			originTimeLte: c.OriginTimeTo,
			statuses: c.ToStatuses(),
			isWarning: c.ToIsWarningQueryValue());

	/// <summary>
	/// 取得した最終報を一覧へ追加する
	/// </summary>
	/// <remarks>ページングで同じ event_id が再び返ることがあるため取り除く</remarks>
	private void AppendItems(IReadOnlyList<Generated.EewItemWithRelations> items)
	{
		foreach (var item in items)
		{
			if (Items.Any(e => e.EventId == item.Event_id))
				continue;
			Items.Add(new EewHistoryListItem(item));
		}
	}

	private void SetSelectedItem(EewHistoryListItem? item)
	{
		if (SelectedItem != null)
			SelectedItem.IsSelecting = false;
		SelectedItem = item;
		if (SelectedItem != null)
			SelectedItem.IsSelecting = true;
	}
}
