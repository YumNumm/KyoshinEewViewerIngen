using DmdataSharp.ApiResponses.V2.Parameters;
using DmdataSharp.Exceptions;
using KyoshinEewViewer.Core;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.JmaXmlParser;
using KyoshinEewViewer.Series.Earthquake.Events;
using KyoshinEewViewer.Series.Earthquake.Models;
using KyoshinEewViewer.Services;
using KyoshinEewViewer.Services.TelegramPublishers;
using KyoshinEewViewer.Services.TelegramPublishers.Dmdata;
using KyoshinMonitorLib;
using ReactiveUI;
using Sentry;
using Splat;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Subjects;
using System.Threading.Tasks;

namespace KyoshinEewViewer.Series.Earthquake.Services;

/// <summary>
/// 地震情報の更新を担う
/// </summary>
public class EarthquakeWatchService : ReactiveObject
{
	private readonly string[] _targetTitles = ["震度速報", "震源に関する情報", "震源・震度に関する情報", "顕著な地震の震源要素更新のお知らせ", "長周期地震動に関する観測情報"];

	public EarthquakeStationParameterResponse? Stations { get; private set; }
	public ObservableCollection<EarthquakeEvent> Earthquakes { get; } = [];

	/// <summary>
	/// <see cref="Earthquakes"/> と各イベントの情報フラグメントを操作するためのロック
	/// </summary>
	/// <remarks>
	/// 電文の受信と EQMonitor API の取得は別々のスレッドから同時に到達しうる。
	/// 排他しないと同一イベントが二重に作られたり、電文由来のフラグメントが
	/// 上書き判定をすり抜けて消えたりする
	/// </remarks>
	private object EarthquakesLock { get; } = new();

	private readonly Subject<EarthquakeUpdate> _earthquakeUpdatedSubject = new();
	private readonly Subject<Unit> _failedSubject = new();
	private readonly Subject<Unit> _sourceSwitchingSubject = new();
	private readonly Subject<string> _sourceSwitchedSubject = new();

	/// <summary>
	/// 地震情報が更新された際に通知される
	/// </summary>
	public IObservable<EarthquakeUpdate> EarthquakeUpdated => _earthquakeUpdatedSubject;
	/// <summary>
	/// 全ての受信元で接続に失敗した際に通知される
	/// </summary>
	public IObservable<Unit> Failed => _failedSubject;
	/// <summary>
	/// 受信元の切り替えが開始された際に通知される
	/// </summary>
	public IObservable<Unit> SourceSwitching => _sourceSwitchingSubject;
	/// <summary>
	/// 受信元の切り替えが完了した際に通知される(引数は受信元名)
	/// </summary>
	public IObservable<string> SourceSwitched => _sourceSwitchedSubject;

	private ILogger Logger { get; }
	private KyoshinEewViewerConfiguration Config { get; }

	/// <summary>
	/// 電文以外の受信元(EQMonitor API など)の名前<br/>
	/// 提供されていない場合は null
	/// </summary>
	private string? ExternalSourceName { get; set; }

	/// <summary>
	/// 電文の受信元がすべて失効しているか
	/// </summary>
	private bool IsTelegramSourceFailed { get; set; }

	/// <summary>
	/// 電文以外の受信元の状態を設定する
	/// </summary>
	/// <remarks>
	/// 電文の受信元がすべて失効していても、こちらが生きていれば地震情報は表示できるため受信エラーとしない
	/// </remarks>
	/// <param name="name">受信元名。利用できなくなった場合は null</param>
	public void SetExternalSource(string? name)
	{
		if (ExternalSourceName == name)
			return;
		ExternalSourceName = name;

		// 電文の受信元が生きている間は表示状態に影響しない
		if (!IsTelegramSourceFailed)
			return;

		if (name == null)
		{
			_failedSubject.OnNext(Unit.Default);
			return;
		}

		Logger.LogInfo($"電文の受信元が失効していますが、{name} から受信しているため表示を継続します");
		NotifyExternalSourceActive(name);
	}

	/// <summary>
	/// 電文以外の受信元へ切り替わったことを通知する
	/// </summary>
	/// <remarks>
	/// 受信エラーの表示は切り替え開始の通知で解除されるため、通常の受信元切り替えと同じ順序で流す
	/// </remarks>
	private void NotifyExternalSourceActive(string name)
	{
		_sourceSwitchingSubject.OnNext(Unit.Default);
		_sourceSwitchedSubject.OnNext(name);
	}

	public EarthquakeWatchService(
		ILogManager logManager,
		KyoshinEewViewerConfiguration config,
		TelegramProvideService telegramProvider,
		DmdataRedundantTelegramPublisher dmdata)
	{
		SplatRegistrations.RegisterLazySingleton<EarthquakeWatchService>();

		Logger = logManager.GetLogger<EarthquakeWatchService>();
		Config = config;

		telegramProvider.Subscribe(
			InformationCategory.Earthquake,
			async (s, t) =>
			{
				// 電文の受信元が使えるようになった
				IsTelegramSourceFailed = false;
				_sourceSwitchingSubject.OnNext(Unit.Default);

				if (s.Contains("DM-D.S.S") && Stations == null)
					try
					{
						Stations = await dmdata.GetEarthquakeStationsAsync();
					}
					catch (DmdataForbiddenException) { }
					catch (Exception ex)
					{
						Logger.LogError(ex, "地震観測点情報取得中に問題が発生しました");
					}

				lock (EarthquakesLock)
					Earthquakes.Clear();
				foreach (var h in t.OrderBy(h => h.ArrivalTime).ToArray())
				{
					try
					{
						await ProcessInformation(h, hideNotice: true);
					}
					catch (Exception ex)
					{
						Logger.LogError(ex, "キャッシュ破損疑いのため削除します");
						try
						{
							// キャッシュ破損時用
							h.Cleanup();
							await ProcessInformation(h, hideNotice: true);
						}
						catch (Exception ex2)
						{
							// その他のエラー発生時は処理を中断させる
							Logger.LogError(ex2, "初回電文取得中に問題が発生しました");
						}
						return;
					}
				}
				// 電文データがない(震源情報しかないなどの)データを削除する
				lock (EarthquakesLock)
					foreach (var eq in Earthquakes.Where(e => e.Fragments.All(f => f is not IntensityInformationFragment and not HypocenterAndIntensityInformationFragment)).ToArray())
						Earthquakes.Remove(eq);

				foreach (var eq in Earthquakes)
					_earthquakeUpdatedSubject.OnNext(new EarthquakeUpdate(eq, IsBulkInserting: true, IsDryRun: false, Fragment: null, PreviousMaxIntensity: null));
				_sourceSwitchedSubject.OnNext(s);
			},
			async t =>
			{
				var trans = SentrySdk.StartTransaction("earthquake", "arrived");
				try
				{
					await ProcessInformation(t);
					trans.Finish();
				}
				catch (Exception ex)
				{
					trans.Finish(ex);
				}
			},
			s =>
			{
				if (!s.isAllFailed)
				{
					_sourceSwitchingSubject.OnNext(Unit.Default);
					return;
				}

				IsTelegramSourceFailed = true;
				// 電文以外の受信元が地震情報を提供していれば受信エラーとしない
				if (ExternalSourceName is { } externalSource)
				{
					Logger.LogInfo($"電文の受信元が失効しましたが、{externalSource} から受信しているため表示を継続します");
					NotifyExternalSourceActive(externalSource);
					return;
				}
				_failedSubject.OnNext(Unit.Default);
			});

		telegramProvider.Subscribe(
			InformationCategory.Tsunami,
			(_, _) =>
			{
				// あくまで震源情報の代わりなので津波情報はとりあえずなにもしない
				// 問題が発生したらなんとかする
				return Task.CompletedTask;
			},
			async t =>
			{
				try
				{
					await ProcessTsunamiInformation(t);
				}
				catch (Exception ex)
				{
					Logger.LogError(ex, "津波情報による震源情報の更新に失敗しました。");
				}
			},
			_ => { }
		);
	}

	/// <summary>
	/// 電文を伴わない受信元(EQMonitor API など)から取得した地震情報を取り込む
	/// </summary>
	/// <remarks>
	/// 同一イベントを電文から構築済みの場合、そちらは観測点ごとの詳細な震度を持つため上書きしない
	/// </remarks>
	/// <returns>取り込まなかった場合は null</returns>
	public EarthquakeEvent? MergeExternalEarthquake(string eventId, EarthquakeInformationFragment fragment)
	{
		EarthquakeEvent target;
		lock (EarthquakesLock)
		{
			if (Earthquakes.FirstOrDefault(e => e.EventId == eventId) is { } existing)
			{
				// 電文由来の情報の方が詳細なため手を加えない
				if (existing.Fragments.Any(f => f.BasedTelegram != null))
					return null;

				existing.ReplaceFragments(fragment);
				target = existing;
			}
			else
			{
				target = new EarthquakeEvent(eventId);
				target.AddFragment(fragment);

				// イベントIDは yyyyMMddHHmmss 形式のため、文字列比較で新しい順に並べられる
				var index = 0;
				while (index < Earthquakes.Count && string.CompareOrdinal(Earthquakes[index].EventId, eventId) > 0)
					index++;
				Earthquakes.Insert(index, target);
			}
		}

		_earthquakeUpdatedSubject.OnNext(new EarthquakeUpdate(target, IsBulkInserting: true, IsDryRun: false, Fragment: fragment, PreviousMaxIntensity: null));
		return target;
	}

	public async Task ProcessTsunamiInformation(Telegram telegram, bool hideNotice = false)
	{
		await using var stream = await telegram.GetBodyAsync();
		using var report = new JmaXmlDocument(stream);
		if (report.Control.Title != "津波警報・注意報・予報a")
			return;

		var fragments = EarthquakeInformationFragment.CreateFromTsunamiJmxXmlDocument(telegram, report);
		foreach (var (eventId, fragment) in fragments)
		{
			// TODO 作成できるようにしておいた方がよさそう
			EarthquakeEvent? eq;
			lock (EarthquakesLock)
			{
				eq = Earthquakes.FirstOrDefault(e => e.EventId == eventId);
				eq?.AddFragment(fragment);
			}
			if (eq == null)
			{
				Logger.LogWarning($"イベントID {eventId} が見つからなかったため津波情報による震源情報の更新を行いませんでした。");
				continue;
			}
			if (!hideNotice)
				_earthquakeUpdatedSubject.OnNext(new EarthquakeUpdate(eq, IsBulkInserting: false, IsDryRun: false, Fragment: null, PreviousMaxIntensity: null));
		}
	}
	public async Task<EarthquakeEvent?> ProcessInformation(Telegram telegram, bool dryRun = false, bool hideNotice = false)
	{
		await using var stream = await telegram.GetBodyAsync();
		using var report = new JmaXmlDocument(stream);

		try
		{
			// サポート外であれば見なかったことにする
			if (!_targetTitles.Contains(report.Control.Title))
				return null;

			bool isCreated;
			EarthquakeEvent eq;
			JmaIntensity prevInt;
			EarthquakeInformationFragment? fragment;
			// EQMonitor API の取り込みと同時に到達しても壊れないよう、
			// イベントの取得･作成からフラグメントの追加までを排他する
			lock (EarthquakesLock)
			{
				isCreated = false;
				// 保存されている Earthquake インスタンスを抜き出してくる
				eq = Earthquakes.FirstOrDefault(e => e.EventId == report.Head.EventId)!;
				if (eq == null || dryRun)
				{
					eq = new EarthquakeEvent(report.Head.EventId);
					if (!dryRun)
						Earthquakes.Insert(0, eq);
					isCreated = true;
				}

				// 情報更新前の震度
				prevInt = eq.Intensity;

				// 情報を処理
				fragment = eq.ProcessTelegram(telegram, report);
			}
			if (!hideNotice)
				_earthquakeUpdatedSubject.OnNext(new EarthquakeUpdate(eq, IsBulkInserting: false, IsDryRun: dryRun, Fragment: fragment, PreviousMaxIntensity: isCreated ? null : prevInt));
			return eq;
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "デシリアライズ時に例外が発生しました");
			return null;
		}
	}
}
