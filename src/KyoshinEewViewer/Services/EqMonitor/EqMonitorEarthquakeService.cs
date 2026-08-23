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
/// EQMonitor WebSocket から地震情報を受信し、初期状態と過去情報を API から取得する
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
	/// 初期同期と WebSocket で同じ内容が届いても取り込み直さないために保持する。
	/// あわせて、受信元の切り替えで一覧が消去された際の復元にも使う
	/// </summary>
	private Dictionary<string, (string Signature, EarthquakeInformationFragment Fragment)> Fetched { get; } = [];
	private object FetchedLock { get; } = new();

	private readonly SemaphoreSlim _fetchLock = new(1, 1);

	/// <summary>
	/// 次に読み込む位置を示すカーソル<br/>
	/// 読み込み済みのうち最も古い位置を指すため、初期同期では更新しない
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
		EqMonitorRealtimeService realtimeService)
	{
		SplatRegistrations.RegisterLazySingleton<EqMonitorEarthquakeService>();

		Logger = logManager.GetLogger<EqMonitorEarthquakeService>();
		Config = config;
		ApiProvider = apiProvider;
		WatchService = watchService;

		// 受信元の切り替え時に一覧が消去されるため、取り込み済みの情報を戻す
		watchService.SourceSwitched.Subscribe(_ => Restore());

		realtimeService.RegisterEarthquakeConsumer(
			SynchronizeAsync,
			ApplyRealtime,
			SetConnected);
		realtimeService.Start();
	}

	/// <summary>
	/// ready 受信後に地震情報の先頭ページを一度だけ同期する
	/// </summary>
	private async Task SynchronizeAsync(CancellationToken cancellationToken)
	{
		var client = ApiProvider.GetClient()
			?? throw new InvalidOperationException("EQMonitor API の接続先が正しくありません");

		await _fetchLock.WaitAsync(cancellationToken);
		try
		{
			var response = await client.GetV2EarthquakeAsync(
				limit: PageSize.ToString(CultureInfo.InvariantCulture),
				cancellationToken: cancellationToken);

			var merged = MergeItems(response.Items);
			if (merged > 0)
				Logger.LogInfo($"EQMonitor API から地震情報を {response.Items.Count} 件取得し {merged} 件を取り込みました");

			// 追加読み込みが進めた位置を巻き戻さないよう、カーソルは未設定のときだけ覚える
			_nextCursor ??= response.Next_token;
			CanLoadMore = _nextCursor != null;
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
		// 初期同期と競合しないよう、こちらは終わるまで待つ
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
			if (item.ToFragment() is not { } fragment)
				continue;
			if (MergeItem(item.Event_id, signature, fragment))
				merged++;
		}
		return merged;
	}

	private bool MergeItem(
		string eventId,
		string signature,
		EarthquakeInformationFragment fragment)
	{
		lock (FetchedLock)
		{
			if (Fetched.TryGetValue(eventId, out var cached) && cached.Signature == signature)
				return false;
			Fetched[eventId] = (signature, fragment);
		}
		return WatchService.MergeExternalEarthquake(eventId, fragment) != null;
	}

	private void ApplyRealtime(EqMonitorRealtimeMessage message)
	{
		switch (message)
		{
			case EqMonitorEarthquakeUpsertMessage upsert:
				var signature = JsonConvert.SerializeObject(upsert.Record);
				if (upsert.Record.ToFragment() is not { } fragment)
					return;
				if (MergeItem(upsert.Record.Event_id, signature, fragment))
					Logger.LogInfo($"EQMonitor から地震情報を受信しました: {upsert.Record.Event_id}");
				break;
			case EqMonitorEarthquakeDeleteMessage delete:
				lock (FetchedLock)
					Fetched.Remove(delete.EventId);
				if (WatchService.RemoveExternalEarthquake(delete.EventId))
					Logger.LogInfo($"EQMonitor から地震情報の削除を受信しました: {delete.EventId}");
				break;
		}
	}

	private void SetConnected(bool connected)
	{
		WatchService.SetExternalSource(connected ? SourceName : null);
		if (!connected && (!Config.EqMonitor.Enable || !Config.EqMonitor.EnableEarthquake))
			CanLoadMore = false;
	}

	/// <summary>
	/// 取り込み済みの情報を一覧へ戻す
	/// </summary>
	private void Restore()
	{
		if (!Config.EqMonitor.Enable || !Config.EqMonitor.EnableEarthquake)
			return;

		KeyValuePair<string, (string Signature, EarthquakeInformationFragment Fragment)>[] fetched;
		lock (FetchedLock)
			fetched = [.. Fetched.OrderBy(x => x.Key, StringComparer.Ordinal)];
		foreach (var (eventId, cached) in fetched)
			WatchService.MergeExternalEarthquake(eventId, cached.Fragment);
	}
}
