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
			var limit = Math.Clamp(Config.EqMonitor.EarthquakeFetchCount, 1, 100);
			var response = await client.GetV2EarthquakeAsync(limit: limit.ToString(CultureInfo.InvariantCulture));

			var merged = 0;
			// 一覧は新しい順に届くため、古いものから順に取り込む
			foreach (var item in response.Items.Reverse())
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
			if (merged > 0)
				Logger.LogInfo($"EQMonitor API から地震情報を {response.Items.Count} 件取得し {merged} 件を取り込みました");
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
