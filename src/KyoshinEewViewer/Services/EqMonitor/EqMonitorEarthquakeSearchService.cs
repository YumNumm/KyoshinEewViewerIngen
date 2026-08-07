using KyoshinEewViewer.Core;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Series.Earthquake.Models;
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
/// EQMonitor API で地震履歴を検索する
/// </summary>
/// <remarks>
/// 通常の受信を担う <see cref="EqMonitorEarthquakeService"/> とは状態を分ける。
/// 検索結果は自身のコレクションに保持し、通常の一覧には書き込まない
/// </remarks>
public class EqMonitorEarthquakeSearchService : ReactiveObject
{
	private ILogger Logger { get; }
	private KyoshinEewViewerConfiguration Config { get; }
	private EqMonitorApiProvider ApiProvider { get; }

	/// <summary>
	/// 検索結果
	/// </summary>
	public ObservableCollection<EarthquakeEvent> Results { get; } = [];

	private readonly SemaphoreSlim _lock = new(1, 1);
	private string? _nextCursor;
	private EqMonitorSearchCondition? _condition;
	private bool _isLoadMoreFailing;

	private bool _canLoadMore;
	/// <summary>
	/// さらに過去の検索結果を読み込めるか
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

	private bool _isCursorUnavailable;
	/// <summary>
	/// 並び順の指定によりページングを行えない状態か
	/// </summary>
	public bool IsCursorUnavailable
	{
		get => _isCursorUnavailable;
		private set => this.RaiseAndSetIfChanged(ref _isCursorUnavailable, value);
	}

	public EqMonitorEarthquakeSearchService(
		ILogManager logManager,
		KyoshinEewViewerConfiguration config,
		EqMonitorApiProvider apiProvider)
	{
		SplatRegistrations.RegisterLazySingleton<EqMonitorEarthquakeSearchService>();

		Logger = logManager.GetLogger<EqMonitorEarthquakeSearchService>();
		Config = config;
		ApiProvider = apiProvider;
	}

	/// <summary>
	/// 条件を指定して検索する
	/// </summary>
	/// <returns>検索できなかった場合はその理由。成功した場合は null</returns>
	public async Task<string?> SearchAsync(EqMonitorSearchCondition condition)
	{
		if (ApiProvider.GetClient() is not { } client)
			return "EQMonitor API の接続先が設定されていません。";

		var normalized = condition.Normalize();

		await _lock.WaitAsync();
		try
		{
			var response = await RequestAsync(client, normalized, cursor: null);

			Results.Clear();
			AppendResults(response.Items);

			_condition = normalized;
			_nextCursor = response.Next_token;
			_isLoadMoreFailing = false;
			IsCursorUnavailable = !normalized.CanUseCursor;
			CanLoadMore = normalized.CanUseCursor && _nextCursor != null;

			Logger.LogInfo($"EQMonitor API で地震履歴を検索し {Results.Count} 件が該当しました");
			return null;
		}
		catch (Exception ex)
		{
			Logger.LogWarning($"EQMonitor API での地震履歴の検索に失敗しました: {ex.Message}");
			return $"検索に失敗しました: {ex.Message}";
		}
		finally
		{
			_lock.Release();
		}
	}

	/// <summary>
	/// 検索結果の続きを読み込む
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
			var response = await RequestAsync(client, condition, cursor);
			AppendResults(response.Items);

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
				Logger.LogWarning($"EQMonitor API での検索結果の追加読み込みに失敗しました: {ex.Message}");
			}
		}
		finally
		{
			_lock.Release();
			IsLoadingMore = false;
		}
	}

	/// <summary>
	/// 検索結果を破棄する
	/// </summary>
	public void Clear()
	{
		Results.Clear();
		_condition = null;
		_nextCursor = null;
		_isLoadMoreFailing = false;
		CanLoadMore = false;
		IsCursorUnavailable = false;
	}

	private Task<Generated.EarthquakeListResponse> RequestAsync(
		Generated.EqMonitorApiClient client, EqMonitorSearchCondition c, string? cursor)
		=> client.GetV2EarthquakeAsync(
			limit: Math.Clamp(Config.EqMonitor.EarthquakeFetchCount, 1, 100).ToString(CultureInfo.InvariantCulture),
			cursor: cursor,
			magnitudeLte: EqMonitorSearchCondition.ToQueryValue(c.MagnitudeTo),
			magnitudeGte: EqMonitorSearchCondition.ToQueryValue(c.MagnitudeFrom),
			depthLte: EqMonitorSearchCondition.ToQueryValue(c.DepthTo),
			depthGte: EqMonitorSearchCondition.ToQueryValue(c.DepthFrom),
			intensityLte: c.IntensityTo,
			intensityGte: c.IntensityFrom,
			maxLpgmIntensityLte: c.LpgmIntensityTo,
			maxLpgmIntensityGte: c.LpgmIntensityFrom,
			originTimeGte: c.OriginTimeFrom,
			originTimeLte: c.OriginTimeTo,
			statuses: c.ToStatuses(),
			epicenterCodes: null,
			epicenterDetailCode: null,
			earthquakeType: c.EarthquakeType,
			datasource: c.Datasource,
			telegramTypes: c.ToTelegramTypes(),
			latitudeGte: EqMonitorSearchCondition.ToQueryValue(c.LatitudeFrom),
			latitudeLte: EqMonitorSearchCondition.ToQueryValue(c.LatitudeTo),
			longitudeGte: EqMonitorSearchCondition.ToQueryValue(c.LongitudeFrom),
			longitudeLte: EqMonitorSearchCondition.ToQueryValue(c.LongitudeTo),
			sortBy: c.SortBy,
			sortOrder: c.SortOrder);

	/// <summary>
	/// 取得した地震情報を検索結果へ追加する
	/// </summary>
	/// <remarks>
	/// API が返した順序をそのまま保つ。並び順の指定を反映するため、
	/// 通常の一覧のようにイベントIDで並べ替えない
	/// </remarks>
	private void AppendResults(IReadOnlyList<Generated.EarthquakePartial> items)
	{
		foreach (var item in items)
		{
			// 並び順によっては同じ地震が再び返ることがあるため取り除く
			if (Results.Any(e => e.EventId == item.Event_id))
				continue;
			if (item.ToFragment() is not { } fragment)
				continue;

			var eq = new EarthquakeEvent(item.Event_id);
			eq.AddFragment(fragment);
			Results.Add(eq);
		}
	}
}
