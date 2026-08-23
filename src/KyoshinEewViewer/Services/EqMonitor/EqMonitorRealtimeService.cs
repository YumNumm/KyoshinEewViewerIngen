using KyoshinEewViewer.Core;
using KyoshinEewViewer.Core.Models;
using Newtonsoft.Json;
using Splat;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Services.EqMonitor;

/// <summary>
/// EQMonitor の端末登録、チケット取得、WebSocket 接続、再接続を一元管理する
/// </summary>
public sealed class EqMonitorRealtimeService : IDisposable
{
	private abstract record WorkItem;
	private sealed record MessageWorkItem(EqMonitorRealtimeMessage Message) : WorkItem;
	private sealed record SynchronizeEewWorkItem : WorkItem;
	private sealed record SynchronizeEarthquakeWorkItem : WorkItem;

	private ILogger Logger { get; }
	private KyoshinEewViewerConfiguration Config { get; }
	private EqMonitorApiProvider ApiProvider { get; }
	internal EqMonitorDeviceService DeviceService { get; set; }
	private object SyncRoot { get; } = new();
	private PropertyChangedEventHandler ConfigChangedHandler { get; }

	private Func<CancellationToken, Task>? _synchronizeEew;
	private Action<Generated.EewItemWithRelations>? _applyEew;
	private Action<bool>? _notifyEewConnection;
	private Func<CancellationToken, Task>? _synchronizeEarthquake;
	private Action<EqMonitorRealtimeMessage>? _applyEarthquake;
	private Action<bool>? _notifyEarthquakeConnection;
	private CancellationTokenSource? _runCancellation;
	private ChannelWriter<WorkItem>? _activeWriter;
	private bool _isReady;
	private bool _isStarted;
	private bool _isDisposed;

	internal Func<IEqMonitorWebSocket> CreateWebSocket { get; set; } =
		() => new EqMonitorClientWebSocket();
	internal Func<TimeSpan, CancellationToken, Task> DelayAsync { get; set; } =
		Task.Delay;

	internal void ReplaceDeviceServiceForTesting(EqMonitorDeviceService deviceService)
	{
		DeviceService.Dispose();
		DeviceService = deviceService;
	}

	public EqMonitorRealtimeService(
		ILogManager logManager,
		KyoshinEewViewerConfiguration config,
		EqMonitorApiProvider apiProvider)
	{
		SplatRegistrations.RegisterLazySingleton<EqMonitorRealtimeService>();

		Logger = logManager.GetLogger<EqMonitorRealtimeService>();
		Config = config;
		ApiProvider = apiProvider;
		DeviceService = new EqMonitorDeviceService(apiProvider, config);

		ConfigChangedHandler = (_, e) =>
		{
			if (e.PropertyName is nameof(Config.EqMonitor.Enable)
				or nameof(Config.EqMonitor.EnableEew)
				or nameof(Config.EqMonitor.EnableEarthquake)
				or nameof(Config.EqMonitor.BaseUrl))
				RestartForConfigurationChange();
		};
		Config.EqMonitor.PropertyChanged += ConfigChangedHandler;
	}

	public void RegisterEewConsumer(
		Func<CancellationToken, Task> synchronize,
		Action<Generated.EewItemWithRelations> apply,
		Action<bool> notifyConnection)
	{
		lock (SyncRoot)
		{
			_synchronizeEew = synchronize;
			_applyEew = apply;
			_notifyEewConnection = notifyConnection;
			if (_isReady)
				_activeWriter?.TryWrite(new SynchronizeEewWorkItem());
			EnsureRunningLocked();
		}
	}

	public void RegisterEarthquakeConsumer(
		Func<CancellationToken, Task> synchronize,
		Action<EqMonitorRealtimeMessage> apply,
		Action<bool> notifyConnection)
	{
		lock (SyncRoot)
		{
			_synchronizeEarthquake = synchronize;
			_applyEarthquake = apply;
			_notifyEarthquakeConnection = notifyConnection;
			if (_isReady)
				_activeWriter?.TryWrite(new SynchronizeEarthquakeWorkItem());
			EnsureRunningLocked();
		}
	}

	/// <summary>
	/// 設定が許可している場合に接続ループを開始する。複数回呼んでも接続は増えない
	/// </summary>
	public void Start()
	{
		lock (SyncRoot)
		{
			if (_isDisposed)
				throw new ObjectDisposedException(nameof(EqMonitorRealtimeService));
			_isStarted = true;
			EnsureRunningLocked();
		}
	}

	private bool ShouldConnectLocked()
		=> Config.EqMonitor.Enable
			&& ApiProvider.GetBaseUri() != null
			&& ((Config.EqMonitor.EnableEew && _applyEew != null)
				|| (Config.EqMonitor.EnableEarthquake && _applyEarthquake != null));

	private void EnsureRunningLocked()
	{
		if (!_isStarted || _isDisposed || _runCancellation != null || !ShouldConnectLocked())
			return;
		StartRunLocked();
	}

	private void StartRunLocked()
	{
		var cancellation = new CancellationTokenSource();
		_runCancellation = cancellation;
		_ = Task.Run(async () =>
		{
			try
			{
				await RunAsync(cancellation);
			}
			finally
			{
				lock (SyncRoot)
				{
					if (_runCancellation == cancellation)
						_runCancellation = null;
				}
				cancellation.Dispose();
			}
		});
	}

	private void RestartForConfigurationChange()
	{
		var shouldNotify = false;
		lock (SyncRoot)
		{
			if (!_isStarted || _isDisposed)
				return;
			shouldNotify = _runCancellation != null || _isReady;
			_runCancellation?.Cancel();
			_runCancellation = null;
			_activeWriter = null;
			_isReady = false;
			EnsureRunningLocked();
		}
		if (shouldNotify)
			NotifyConnection(false);
	}

	private async Task RunAsync(CancellationTokenSource owner)
	{
		var cancellationToken = owner.Token;
		var retryCount = 0;
		var invalidDeviceRetried = false;
		var failureLogged = false;
		while (!cancellationToken.IsCancellationRequested)
		{
			IEqMonitorWebSocket? socket = null;
			try
			{
				await DeviceService.GetOrRegisterDeviceIdAsync(cancellationToken);
				var client = ApiProvider.GetClient()
					?? throw new InvalidOperationException("EQMonitor API の接続先が正しくありません");

				Generated.RealtimeTicketResponse ticket;
				try
				{
					ticket = await client.GetV2RealtimeTicketAsync(cancellationToken);
				}
				catch (Generated.ApiException ex)
					when (ex.StatusCode is 401 or 404 && !invalidDeviceRetried)
				{
					invalidDeviceRetried = true;
					DeviceService.InvalidateRegistration();
					continue;
				}

				invalidDeviceRetried = false;
				socket = CreateWebSocket();
				await socket.ConnectAsync(
					ticket.Url,
					new Dictionary<string, string>
					{
						["User-Agent"] = EqMonitorApiProvider.UserAgent,
						["x-eqmonitor-build"] = EqMonitorApiProvider.BuildNumber,
					},
					cancellationToken);
				await RunConnectionAsync(socket, owner, () =>
				{
					retryCount = 0;
					if (failureLogged)
						Logger.LogInfo("EQMonitor WebSocket へ再接続しました");
					failureLogged = false;
				}, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				if (!failureLogged)
				{
					Logger.LogWarning(ex, "EQMonitor WebSocket へ接続できませんでした。自動的に再接続します");
					failureLogged = true;
				}
			}
			finally
			{
				SetDisconnected(owner);
				if (socket != null)
				{
					try
					{
						await socket.CloseAsync(CancellationToken.None);
					}
					catch (Exception ex)
					{
						Logger.LogDebug(ex, "EQMonitor WebSocket の切断処理に失敗しました");
					}
					await socket.DisposeAsync();
				}
			}

			if (cancellationToken.IsCancellationRequested)
				break;
			var delay = GetReconnectDelay(retryCount++);
			try
			{
				await DelayAsync(delay, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			invalidDeviceRetried = false;
		}
	}

	private async Task RunConnectionAsync(
		IEqMonitorWebSocket socket,
		CancellationTokenSource owner,
		Action ready,
		CancellationToken cancellationToken)
	{
		var channel = Channel.CreateUnbounded<WorkItem>(new UnboundedChannelOptions
		{
			SingleReader = true,
			SingleWriter = false,
		});
		lock (SyncRoot)
		{
			if (_runCancellation == owner)
				_activeWriter = channel.Writer;
		}

		var receiver = ReceiveAsync(socket, channel.Writer, cancellationToken);
		var processor = ProcessAsync(channel.Reader, owner, ready, cancellationToken);
		await receiver;
		channel.Writer.TryComplete();
		await processor;
	}

	private async Task ReceiveAsync(
		IEqMonitorWebSocket socket,
		ChannelWriter<WorkItem> writer,
		CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			var json = await socket.ReceiveTextAsync(cancellationToken);
			if (json == null)
				return;

			EqMonitorRealtimeMessage message;
			try
			{
				message = EqMonitorRealtimeMessageParser.Parse(json);
			}
			catch (JsonException ex)
			{
				Logger.LogWarning(ex, "EQMonitor WebSocket から不正なメッセージを受信しました");
				continue;
			}

			if (message is EqMonitorPingMessage)
			{
				await socket.SendTextAsync("""{"type":"pong"}""", cancellationToken);
				continue;
			}
			await writer.WriteAsync(new MessageWorkItem(message), cancellationToken);
		}
	}

	private async Task ProcessAsync(
		ChannelReader<WorkItem> reader,
		CancellationTokenSource owner,
		Action ready,
		CancellationToken cancellationToken)
	{
		await foreach (var work in reader.ReadAllAsync(cancellationToken))
		{
			switch (work)
			{
				case MessageWorkItem { Message: EqMonitorReadyMessage }:
					var isCurrent = false;
					Func<CancellationToken, Task>? synchronizeEew = null;
					Action<bool>? notifyEewConnection = null;
					Func<CancellationToken, Task>? synchronizeEarthquake = null;
					Action<bool>? notifyEarthquakeConnection = null;
					lock (SyncRoot)
					{
						if (_runCancellation == owner)
						{
							_isReady = true;
							isCurrent = true;
							// ready 処理中に購読者が追加されても二重同期しないよう、
							// この時点で登録済みの購読者だけをスナップショットする
							if (Config.EqMonitor.EnableEew)
							{
								synchronizeEew = _synchronizeEew;
								notifyEewConnection = _notifyEewConnection;
							}
							if (Config.EqMonitor.EnableEarthquake)
							{
								synchronizeEarthquake = _synchronizeEarthquake;
								notifyEarthquakeConnection = _notifyEarthquakeConnection;
							}
						}
					}
					if (!isCurrent)
						return;
					ready();
					NotifyConsumerConnection(notifyEewConnection, true, "EEW");
					NotifyConsumerConnection(notifyEarthquakeConnection, true, "地震情報");
					await SynchronizeEewAsync(synchronizeEew, cancellationToken);
					await SynchronizeEarthquakeAsync(synchronizeEarthquake, cancellationToken);
					break;
				case SynchronizeEewWorkItem when Config.EqMonitor.EnableEew:
					NotifyConsumerConnection(_notifyEewConnection, true, "EEW");
					await SynchronizeEewAsync(_synchronizeEew, cancellationToken);
					break;
				case SynchronizeEarthquakeWorkItem when Config.EqMonitor.EnableEarthquake:
					NotifyConsumerConnection(_notifyEarthquakeConnection, true, "地震情報");
					await SynchronizeEarthquakeAsync(_synchronizeEarthquake, cancellationToken);
					break;
				case MessageWorkItem { Message: EqMonitorEewUpsertMessage eew }
					when Config.EqMonitor.EnableEew:
					ApplyEew(eew.Record);
					break;
				case MessageWorkItem
					{
						Message: EqMonitorEarthquakeUpsertMessage or EqMonitorEarthquakeDeleteMessage
					} earthquake when Config.EqMonitor.EnableEarthquake:
					ApplyEarthquake(earthquake.Message);
					break;
				case MessageWorkItem { Message: EqMonitorUnsupportedMessage unsupported }:
					Logger.LogWarning(
						$"未対応の EQMonitor WebSocket メッセージを受信しました: {unsupported.Type}/{unsupported.Operation}");
					break;
			}
		}
	}

	private void ApplyEew(Generated.EewItemWithRelations record)
	{
		try
		{
			_applyEew?.Invoke(record);
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex, "EQMonitor EEW のリアルタイム更新を適用できませんでした");
		}
	}

	private void ApplyEarthquake(EqMonitorRealtimeMessage message)
	{
		try
		{
			_applyEarthquake?.Invoke(message);
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex, "EQMonitor 地震情報のリアルタイム更新を適用できませんでした");
		}
	}

	private async Task SynchronizeEewAsync(
		Func<CancellationToken, Task>? synchronize,
		CancellationToken cancellationToken)
	{
		if (synchronize == null)
			return;
		try
		{
			await synchronize(cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex, "EQMonitor EEW の初期同期に失敗しました。WebSocket 更新を継続します");
		}
	}

	private async Task SynchronizeEarthquakeAsync(
		Func<CancellationToken, Task>? synchronize,
		CancellationToken cancellationToken)
	{
		if (synchronize == null)
			return;
		try
		{
			await synchronize(cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex, "EQMonitor 地震情報の初期同期に失敗しました。WebSocket 更新を継続します");
		}
	}

	private void SetDisconnected(CancellationTokenSource owner)
	{
		var shouldNotify = false;
		lock (SyncRoot)
		{
			if (_runCancellation == owner)
			{
				_activeWriter = null;
				_isReady = false;
				shouldNotify = true;
			}
		}
		if (shouldNotify)
			NotifyConnection(false);
	}

	private void NotifyConnection(bool connected)
	{
		if (!connected || Config.EqMonitor.EnableEew)
			NotifyConsumerConnection(_notifyEewConnection, connected, "EEW");
		if (!connected || Config.EqMonitor.EnableEarthquake)
			NotifyConsumerConnection(_notifyEarthquakeConnection, connected, "地震情報");
	}

	private void NotifyConsumerConnection(
		Action<bool>? notifyConnection,
		bool connected,
		string consumerName)
	{
		try
		{
			notifyConnection?.Invoke(connected);
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex, $"EQMonitor {consumerName} の接続状態通知に失敗しました");
		}
	}

	internal static TimeSpan GetReconnectDelay(int retryCount)
		=> TimeSpan.FromSeconds(Math.Min(1 << Math.Min(retryCount, 6), 60));

	public void Dispose()
	{
		lock (SyncRoot)
		{
			if (_isDisposed)
				return;
			_isDisposed = true;
			_isStarted = false;
			_runCancellation?.Cancel();
			_runCancellation = null;
			_activeWriter = null;
			_isReady = false;
		}
		Config.EqMonitor.PropertyChanged -= ConfigChangedHandler;
		NotifyConnection(false);
		DeviceService.Dispose();
	}
}
