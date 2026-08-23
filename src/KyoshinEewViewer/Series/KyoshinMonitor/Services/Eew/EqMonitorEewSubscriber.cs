using KyoshinEewViewer.Core;
using KyoshinEewViewer.Services;
using KyoshinEewViewer.Services.EqMonitor;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KyoshinEewViewer.Series.KyoshinMonitor.Services.Eew;

/// <summary>
/// EQMonitor WebSocket から緊急地震速報を受信する
/// </summary>
public class EqMonitorEewSubscriber : ReactiveObject
{
	private ILogger Logger { get; }
	private EqMonitorApiProvider ApiProvider { get; }
	private EewController EewController { get; }
	private TimerService TimerService { get; }

	/// <summary>
	/// 取り込み済みのイベントIDと報数<br/>
	/// 同じ内容を繰り返し処理しないために保持する
	/// </summary>
	private Dictionary<string, int> ProcessedSerials { get; } = [];

	private bool _isDisconnected = true;
	/// <summary>
	/// 直近の取得に失敗しているか
	/// </summary>
	public bool IsDisconnected
	{
		get => _isDisconnected;
		set => this.RaiseAndSetIfChanged(ref _isDisconnected, value);
	}

	public EqMonitorEewSubscriber(
		ILogger<EqMonitorEewSubscriber> logger,
		EqMonitorApiProvider apiProvider,
		EqMonitorRealtimeService realtimeService,
		EewController eewController,
		TimerService timerService)
	{
		Logger = logger;
		ApiProvider = apiProvider;
		EewController = eewController;
		TimerService = timerService;

		realtimeService.RegisterEewConsumer(
			SynchronizeAsync,
			Apply,
			connected => IsDisconnected = !connected);
		realtimeService.Start();
	}

	/// <summary>
	/// ready 受信後に最新状態を一度だけ同期する
	/// </summary>
	private async Task SynchronizeAsync(CancellationToken cancellationToken)
	{
		var client = ApiProvider.GetClient()
			?? throw new InvalidOperationException("EQMonitor API の接続先が正しくありません");
		var response = await client.GetV2EewLatestAsync(cancellationToken);
		foreach (var item in response.Items)
			Apply(item);

		// 5分を過ぎて一覧から消えたイベントの記録を捨てる
		if (ProcessedSerials.Count > 0 && response.Items.Count == 0)
			ProcessedSerials.Clear();
	}

	private void Apply(EqMonitorApi.Generated.EewItemWithRelations item)
	{
		var receiveTime = TimerService.CurrentTime;
		var serialNo = (int)item.Serial_no;
		if (ProcessedSerials.TryGetValue(item.Event_id, out var processed) && processed >= serialNo)
			return;
		ProcessedSerials[item.Event_id] = serialNo;

		if (item.Is_canceled)
		{
			Logger.LogInformation($"EQMonitor から EEW 取消報を受信しました: {item.Event_id}");
			EewController.Cancelled(item.Event_id, receiveTime);
			return;
		}

		Logger.LogInformation($"EQMonitor から EEW を受信しました: {item.Event_id} 第{serialNo}報");
		EewController.Update(item.ToEew(receiveTime), receiveTime);
	}
}
