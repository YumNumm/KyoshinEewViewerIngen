using KyoshinEewViewer.Core;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Series.Earthquake.Models;
using KyoshinEewViewer.Series.Earthquake.Services;
using Newtonsoft.Json;
using ReactiveUI;
using Splat;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Services.EqMonitor;

/// <summary>
/// EQMonitor API から地震情報の一覧を取得して取り込む
/// </summary>
public class EqMonitorEarthquakeService : ReactiveObject
{
	/// <summary>
	/// 受信元として表示する名前
	/// </summary>
	private const string SourceName = "EQMonitor";

	private ILogger Logger { get; }
	private KyoshinEewViewerConfiguration Config { get; }
	private EqMonitorApiProvider ApiProvider { get; }
	private EarthquakeWatchService WatchService { get; }

	/// <summary>
	/// 取り込み済みのフラグメントと、取得元の内容を表す署名<br/>
	/// 数秒ごとに同じ内容が届くため、変化のないイベントを取り込み直さないために保持する。
	/// あわせて、受信元の切り替えで一覧が消去された際の復元にも使う
	/// </summary>
	private Dictionary<string, (string Signature, EarthquakeInformationFragment Fragment)> Fetched { get; } = [];

	private readonly SemaphoreSlim _fetchLock = new(1, 1);
	private DateTime _lastFetchedAt = DateTime.MinValue;
	private bool _isFailing;

	/// <summary>
	/// 次に読み込む位置を示すカーソル<br/>
	/// 読み込み済みのうち最も古い位置を指すため、定期取得では更新しない
	/// </summary>
	private string? _nextCursor;

	private bool _canLoadMore;
	/// <summary>
	/// さらに過去の地震情報を読み込めるか
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

	public EqMonitorEarthquakeService(
		ILogManager logManager,
		KyoshinEewViewerConfiguration config,
		EqMonitorApiProvider apiProvider,
		EarthquakeWatchService watchService,
		TimerService timerService)
	{
		SplatRegistrations.RegisterLazySingleton<EqMonitorEarthquakeService>();

		Logger = logManager.GetLogger<EqMonitorEarthquakeService>();
		Config = config;
		ApiProvider = apiProvider;
		WatchService = watchService;

		// 受信元の切り替え時に一覧が消去されるため、取り込み済みの情報を戻す
		watchService.SourceSwitched.Subscribe(_ => Restore());

		// タイマは他の機能が起動していないと回らないため、有効化された時点で起動しておく
		Config.EqMonitor.WhenAnyValue(x => x.Enable, x => x.EnableEarthquake)
			.Where(x => x is { Item1: true, Item2: true })
			.Subscribe(_ => timerService.StartMainTimer());

		timerService.TimerElapsed += t =>
		{
			if (!Config.EqMonitor.Enable || !Config.EqMonitor.EnableEarthquake)
			{
				WatchService.SetExternalSource(null);
				CanLoadMore = false;
				return;
			}
			// タイマは1秒間隔のため、これより短い設定でも1秒間隔になる
			if (t - _lastFetchedAt < TimeSpan.FromMilliseconds(Math.Max(Config.EqMonitor.EarthquakePollingIntervalMs, 1000)))
				return;
			_lastFetchedAt = t;
			_ = FetchAsync();
		};
	}

	/// <summary>
	/// 地震情報の一覧を取得して取り込む
	/// </summary>
	public async Task FetchAsync()
	{
		if (ApiProvider.GetClient() is not { } client)
		{
			WatchService.SetExternalSource(null);
			return;
		}

		// 前回の取得が終わっていなければ見送る
		if (!await _fetchLock.WaitAsync(0))
			return;
		try
		{
			var response = await client.GetV2EarthquakeAsync(limit: PageSize.ToString(CultureInfo.InvariantCulture));

			var merged = MergeItems(response.Items);
			if (merged > 0)
				Logger.LogInfo($"EQMonitor API から地震情報を {response.Items.Count} 件取得し {merged} 件を取り込みました");

			// 追加読み込みが進めた位置を巻き戻さないよう、カーソルは未設定のときだけ覚える
			_nextCursor ??= response.Next_token;
			CanLoadMore = _nextCursor != null;
			_isFailing = false;
			// 電文の受信元が失効していても、こちらが生きていれば受信エラーにしない
			WatchService.SetExternalSource(SourceName);
		}
		catch (Exception ex)
		{
			WatchService.SetExternalSource(null);
			// 数秒ごとに取得するため、復帰するまでは最初の1回だけ記録する
			if (!_isFailing)
			{
				_isFailing = true;
				Logger.LogWarning($"EQMonitor API からの地震情報の取得に失敗しました: {ex.Message}");
			}
		}
		finally
		{
			_fetchLock.Release();
		}
	}

	/// <summary>
	/// さらに過去の地震情報を読み込む
	/// </summary>
	public async Task LoadMoreAsync()
	{
		if (!Config.EqMonitor.Enable || !Config.EqMonitor.EnableEarthquake)
			return;
		if (!CanLoadMore || IsLoadingMore)
			return;
		if (_nextCursor is not { } cursor)
			return;
		if (ApiProvider.GetClient() is not { } client)
			return;

		IsLoadingMore = true;
		// 定期取得と競合しないよう、こちらは終わるまで待つ
		await _fetchLock.WaitAsync();
		try
		{
			var response = await client.GetV2EarthquakeAsync(
				limit: PageSize.ToString(CultureInfo.InvariantCulture),
				cursor: cursor);

			var merged = MergeItems(response.Items);
			_nextCursor = response.Next_token;
			// 空のページが返ってきた時点で終端とみなす
			CanLoadMore = _nextCursor != null && response.Items.Count > 0;
			Logger.LogInfo($"EQMonitor API から過去の地震情報を {response.Items.Count} 件読み込み {merged} 件を取り込みました");
		}
		catch (Exception ex)
		{
			Logger.LogWarning($"EQMonitor API からの過去の地震情報の読み込みに失敗しました: {ex.Message}");
		}
		finally
		{
			_fetchLock.Release();
			IsLoadingMore = false;
		}
	}

	/// <summary>
	/// 指定したイベントの詳細を取得する
	/// </summary>
	/// <remarks>
	/// 一覧には観測点ごとの震度が含まれないため、表示する際に個別に取得する
	/// </remarks>
	/// <returns>取得できなかった場合は null</returns>
	public async Task<Generated.Earthquake?> GetDetailAsync(string eventId)
	{
		if (!Config.EqMonitor.Enable || !Config.EqMonitor.EnableEarthquake)
			return null;
		if (ApiProvider.GetClient() is not { } client)
			return null;

		try
		{
			var response = await client.GetV2EarthquakeByEventIdAsync(eventId);
			return response.Earthquake;
		}
		catch (Exception ex)
		{
			Logger.LogWarning($"EQMonitor API からの地震情報 {eventId} の詳細取得に失敗しました: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// 1 回の取得で読み込む件数
	/// </summary>
	private int PageSize => Math.Clamp(Config.EqMonitor.EarthquakeFetchCount, 1, 100);

	/// <summary>
	/// 取得した地震情報を一覧へ取り込む
	/// </summary>
	/// <returns>取り込んだ件数</returns>
	private int MergeItems(IReadOnlyList<Generated.EarthquakePartial> items)
	{
		var merged = 0;
		// 一覧は新しい順に届くため、古いものから順に取り込む
		foreach (var item in items.Reverse())
		{
			// 前回から内容が変わっていないイベントは触らない
			var signature = JsonConvert.SerializeObject(item);
			if (Fetched.TryGetValue(item.Event_id, out var cached) && cached.Signature == signature)
				continue;
			if (item.ToFragment() is not { } fragment)
				continue;

			Fetched[item.Event_id] = (signature, fragment);
			if (WatchService.MergeExternalEarthquake(item.Event_id, fragment) != null)
				merged++;
		}
		return merged;
	}

	/// <summary>
	/// 取り込み済みの情報を一覧へ戻す
	/// </summary>
	private void Restore()
	{
		if (!Config.EqMonitor.Enable || !Config.EqMonitor.EnableEarthquake)
			return;

		foreach (var (eventId, cached) in Fetched.OrderBy(x => x.Key, StringComparer.Ordinal))
			WatchService.MergeExternalEarthquake(eventId, cached.Fragment);
	}
}
