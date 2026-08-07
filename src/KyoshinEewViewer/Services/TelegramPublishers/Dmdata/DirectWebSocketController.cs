using DmdataSharp.WebSocketMessages.V2;
using KyoshinEewViewer.Core;
using KyoshinEewViewer.Services.NetworkDebug;
using Splat;
using System;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KyoshinEewViewer.Services.TelegramPublishers.Dmdata;

/// <summary>
/// 認証・Ticket発行をスキップして直接WebSocket URLに接続するコントローラー
/// </summary>
public class DirectWebSocketController : IDisposable
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
	};

	private ILogger Logger { get; }
	private ClientWebSocket? _webSocket;
	private CancellationTokenSource? _cts;

	public event EventHandler<DataWebSocketMessage>? DataReceived;
	public event EventHandler? Connected;
	public event EventHandler? Disconnected;
	/// <summary>ping 受信から pong 送信完了までの時間が更新された</summary>
	public event EventHandler? PingLatencyUpdated;

	private bool _isConnected;
	public bool IsConnected => _isConnected;

	private long _totalMessagesReceived;
	public long TotalMessagesReceived => _totalMessagesReceived;

	private DateTime? _lastMessageTime;
	public DateTime? LastMessageTime => _lastMessageTime;

	/// <summary>直近の ping 受信から pong 送信完了までの時間（ミリ秒）。サーバ往復 RTT ではありません。</summary>
	public long? LastPongSendMilliseconds { get; private set; }

	/// <summary>前回の ping からの経過時間（秒）。初回は null。</summary>
	public double? LastPingIntervalSeconds { get; private set; }

	private DateTimeOffset? _previousPingAt;

	public string? EndpointUrl { get; private set; }

	public DirectWebSocketController(ILogManager logManager)
	{
		Logger = logManager.GetLogger<DirectWebSocketController>();
	}

	/// <summary>
	/// 指定された URL に直接接続する
	/// </summary>
	public async Task ConnectAsync(string url)
	{
		EndpointUrl = url;
		_isConnected = false;
		LastPongSendMilliseconds = null;
		LastPingIntervalSeconds = null;
		_previousPingAt = null;

		_cts?.Cancel();
		_cts?.Dispose();
		_cts = new CancellationTokenSource();

		_webSocket?.Dispose();
		_webSocket = new ClientWebSocket();

		Logger.LogInfo($"直接WebSocket接続を開始します: {url}");
		await _webSocket.ConnectAsync(new Uri(url), _cts.Token);
		Logger.LogInfo("直接WebSocket接続が確立されました（start メッセージを待機中）");
		NetworkDebugRecorder.RecordWebSocket(url, WebSocketDirection.Connect);

		// バックグラウンドで受信ループ開始
		_ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
	}

	private async Task ReceiveLoopAsync(CancellationToken ct)
	{
		var buffer = new byte[65536];
		using var messageBuffer = new System.IO.MemoryStream();

		try
		{
			while (!ct.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
			{
				messageBuffer.SetLength(0);
				WebSocketReceiveResult result;

				do
				{
					result = await _webSocket.ReceiveAsync(buffer, ct);

					if (result.MessageType == WebSocketMessageType.Close)
					{
						Logger.LogInfo("WebSocketサーバーから切断要求を受信しました");
						NetworkDebugRecorder.RecordWebSocket(EndpointUrl ?? "", WebSocketDirection.Disconnect, result.CloseStatusDescription);
						try
						{
							await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
						}
						catch { }
						_isConnected = false;
						Disconnected?.Invoke(this, EventArgs.Empty);
						return;
					}

					messageBuffer.Write(buffer, 0, result.Count);
				} while (!result.EndOfMessage);

				var json = Encoding.UTF8.GetString(messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
				NetworkDebugRecorder.RecordWebSocket(EndpointUrl ?? "", WebSocketDirection.Receive, json, messageBuffer.Length);
				await ProcessMessageAsync(json, ct);
			}
		}
		catch (OperationCanceledException)
		{
			// 正常な切断
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex, "直接WebSocket受信中にエラーが発生しました");
			_isConnected = false;
			Disconnected?.Invoke(this, EventArgs.Empty);
		}
	}

	private async Task ProcessMessageAsync(string json, CancellationToken ct)
	{
		try
		{
			using var doc = JsonDocument.Parse(json);
			if (!doc.RootElement.TryGetProperty("type", out var typeProp))
				return;

			var type = typeProp.GetString();
			switch (type)
			{
				case "start":
					Logger.LogInfo("直接WebSocket: start メッセージを受信しました");
					_isConnected = true;
					Connected?.Invoke(this, EventArgs.Empty);
					break;

				case "ping":
				{
					var now = DateTimeOffset.UtcNow;
					if (_previousPingAt is { } prev)
						LastPingIntervalSeconds = (now - prev).TotalSeconds;
					_previousPingAt = now;

					var pingId = doc.RootElement.TryGetProperty("pingId", out var pid) ? pid.GetString() : null;
					var pong = JsonSerializer.Serialize(new { type = "pong", pingId }, JsonOptions);
					var sw = Stopwatch.StartNew();
					if (_webSocket?.State == WebSocketState.Open)
					{
						await _webSocket.SendAsync(
							Encoding.UTF8.GetBytes(pong),
							WebSocketMessageType.Text,
							true,
							ct);
					}
					sw.Stop();
					LastPongSendMilliseconds = sw.ElapsedMilliseconds;
					// 計測値へ影響させないため、記録は Pong 送信の計測を終えてから行う
					NetworkDebugRecorder.RecordWebSocket(EndpointUrl ?? "", WebSocketDirection.Send, pong, Encoding.UTF8.GetByteCount(pong));
					PingLatencyUpdated?.Invoke(this, EventArgs.Empty);
					break;
				}

				case "data":
					var message = JsonSerializer.Deserialize<DataWebSocketMessage>(json, JsonOptions);
					if (message != null)
					{
						Interlocked.Increment(ref _totalMessagesReceived);
						_lastMessageTime = DateTime.Now;
						DataReceived?.Invoke(this, message);
					}
					break;

				case "error":
					var errMsg = doc.RootElement.TryGetProperty("error", out var errProp)
						? errProp.GetString()
						: "不明なエラー";
					Logger.LogWarning($"直接WebSocket: サーバーからエラーを受信しました: {errMsg}");
					break;
			}
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex, "直接WebSocketメッセージの処理に失敗しました");
		}
	}

	/// <summary>
	/// WebSocket接続を切断する
	/// </summary>
	public async Task DisconnectAsync()
	{
		_cts?.Cancel();
		if (_webSocket?.State == WebSocketState.Open)
		{
			try
			{
				await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
			}
			catch { }
		}
		_isConnected = false;
		NetworkDebugRecorder.RecordWebSocket(EndpointUrl ?? "", WebSocketDirection.Disconnect);
		_webSocket?.Dispose();
		_webSocket = null;
		LastPongSendMilliseconds = null;
		LastPingIntervalSeconds = null;
		_previousPingAt = null;
	}

	public void Dispose()
	{
		_cts?.Cancel();
		_cts?.Dispose();
		_webSocket?.Dispose();
		GC.SuppressFinalize(this);
	}
}
