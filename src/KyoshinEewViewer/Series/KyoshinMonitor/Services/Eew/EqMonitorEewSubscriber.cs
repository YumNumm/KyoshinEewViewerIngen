using KyoshinEewViewer.Core;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Services;
using KyoshinEewViewer.Services.EqMonitor;
using ReactiveUI;
using Splat;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KyoshinEewViewer.Series.KyoshinMonitor.Services.Eew;

/// <summary>
/// EQMonitor API から緊急地震速報をポーリングで取得する
/// </summary>
/// <remarks>
/// リアルタイム配信の WebSocket は端末登録が必要なため、
/// 発表から5分以内の最新報を返す /v2/eew/latest を定期的に取得する
/// </remarks>
public class EqMonitorEewSubscriber : ReactiveObject
{
	private ILogger Logger { get; }
	private KyoshinEewViewerConfiguration Config { get; }
	private EqMonitorApiProvider ApiProvider { get; }
	private EewController EewController { get; }

	/// <summary>
	/// 取り込み済みのイベントIDと報数<br/>
	/// 同じ内容を繰り返し処理しないために保持する
	/// </summary>
	private Dictionary<string, int> ProcessedSerials { get; } = [];

	private readonly SemaphoreSlim _pollLock = new(1, 1);
	private DateTime _lastPolledAt = DateTime.MinValue;

	private bool _isDisconnected;
	/// <summary>
	/// 直近の取得に失敗しているか
	/// </summary>
	public bool IsDisconnected
	{
		get => _isDisconnected;
		set => this.RaiseAndSetIfChanged(ref _isDisconnected, value);
	}

	public EqMonitorEewSubscriber(
		ILogManager logManager,
		KyoshinEewViewerConfiguration config,
		EqMonitorApiProvider apiProvider,
		EewController eewController,
		TimerService timerService)
	{
		Logger = logManager.GetLogger<EqMonitorEewSubscriber>();
		Config = config;
		ApiProvider = apiProvider;
		EewController = eewController;

		timerService.TimerElapsed += t =>
		{
			if (!Config.EqMonitor.Enable || !Config.EqMonitor.EnableEew)
				return;
			// タイマは1秒間隔のため、これより短い設定でも1秒間隔になる
			if (t - _lastPolledAt < TimeSpan.FromMilliseconds(Math.Max(Config.EqMonitor.EewPollingIntervalMs, 1000)))
				return;
			_lastPolledAt = t;
			_ = PollAsync(t);
		};
	}

	private async Task PollAsync(DateTime receiveTime)
	{
		if (ApiProvider.GetClient() is not { } client)
			return;

		// 前回の取得が終わっていなければ見送る
		if (!await _pollLock.WaitAsync(0))
			return;
		try
		{
			var response = await client.GetV2EewLatestAsync();
			IsDisconnected = false;

			foreach (var item in response.Items)
			{
				var serialNo = (int)item.Serial_no;
				if (ProcessedSerials.TryGetValue(item.Event_id, out var processed) && processed >= serialNo)
					continue;
				ProcessedSerials[item.Event_id] = serialNo;

				if (item.Is_canceled)
				{
					Logger.LogInfo($"EQMonitor から EEW 取消報を受信しました: {item.Event_id}");
					EewController.Cancelled(item.Event_id, receiveTime);
					continue;
				}

				Logger.LogInfo($"EQMonitor から EEW を受信しました: {item.Event_id} 第{serialNo}報");
				EewController.Update(item.ToEew(receiveTime), receiveTime);
			}

			// 5分を過ぎて一覧から消えたイベントの記録を捨てる
			if (ProcessedSerials.Count > 0 && response.Items.Count == 0)
				ProcessedSerials.Clear();
		}
		catch (Exception ex)
		{
			// 毎秒取得するため、復帰するまでは最初の1回だけ記録する
			if (!IsDisconnected)
				Logger.LogWarning($"EQMonitor API からの緊急地震速報の取得に失敗しました: {ex.Message}");
			IsDisconnected = true;
		}
		finally
		{
			_pollLock.Release();
		}
	}
}
