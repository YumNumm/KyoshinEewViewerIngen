using DmdataSharp;
using DmdataSharp.ApiParameters.V2;
using DmdataSharp.Interfaces;
using DmdataSharp.Redundancy;
using DmdataSharp.WebSocketMessages.V2;
using KyoshinEewViewer.Core;
using ReactiveUI;
using Splat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace KyoshinEewViewer.Services.TelegramPublishers.Dmdata;

/// <summary>
/// WebSocket/PULL接続の管理を担当する
/// </summary>
public class DmdataConnectionManager : ReactiveObject, IDisposable
{
	private ILogger Logger { get; }

	// カテゴリからカテゴリへのマップ
	private static readonly Dictionary<InformationCategory, TelegramCategoryV1> TelegramCategoryMap = new()
	{
		{ InformationCategory.Earthquake, TelegramCategoryV1.Earthquake },
		{ InformationCategory.Tsunami, TelegramCategoryV1.Earthquake },
		{ InformationCategory.Typhoon, TelegramCategoryV1.Weather },
		{ InformationCategory.EewForecast, TelegramCategoryV1.EewForecast },
		{ InformationCategory.EewWarning, TelegramCategoryV1.EewWarning },
	};

	private IRedundantDmdataSocketController? _redundantController;
	public IRedundantDmdataSocketController? RedundantController
	{
		get => _redundantController;
		private set => this.RaiseAndSetIfChanged(ref _redundantController, value);
	}

	private DirectWebSocketController? _directController;
	private DirectWebSocketController? DirectController
	{
		get => _directController;
		set => this.RaiseAndSetIfChanged(ref _directController, value);
	}

	/// <summary>
	/// 直接接続モードかどうか
	/// </summary>
	public bool IsDirectMode { get; private set; }

	/// <summary>
	/// 冗長性状態
	/// </summary>
	private RedundancyStatus _redundancyStatus = RedundancyStatus.Disconnected;
	public RedundancyStatus RedundancyStatus
	{
		get => _redundancyStatus;
		private set => this.RaiseAndSetIfChanged(ref _redundancyStatus, value);
	}

	/// <summary>
	/// アクティブ接続数
	/// </summary>
	private int _activeConnectionCount = 0;
	public int ActiveConnectionCount
	{
		get => _activeConnectionCount;
		private set => this.RaiseAndSetIfChanged(ref _activeConnectionCount, value);
	}

	/// <summary>
	/// 接続中のエンドポイント
	/// </summary>
	private string[] _connectedEndpoints = [];
	public string[] ConnectedEndpoints
	{
		get => _connectedEndpoints;
		private set => this.RaiseAndSetIfChanged(ref _connectedEndpoints, value);
	}

	/// <summary>
	/// 直近の ping 受信から pong 送信完了までの時間（ミリ秒）。直接接続モード時のみ。DM-D.S.S の仕様上サーバ往復 RTT ではありません。
	/// </summary>
	public long? LastPongSendMilliseconds =>
		IsDirectMode ? _directController?.LastPongSendMilliseconds : null;

	/// <summary>
	/// 前回の ping からの経過時間（秒）。初回 ping 後は null。
	/// </summary>
	public double? LastPingIntervalSeconds =>
		IsDirectMode ? _directController?.LastPingIntervalSeconds : _lastRedundantPingIntervalSeconds;

	private double? _lastRedundantPingIntervalSeconds;
	private DateTimeOffset? _previousRedundantPingAt;

	/// <summary>
	/// 受信した総メッセージ数
	/// </summary>
	private long _totalMessagesReceived = 0;
	public long TotalMessagesReceived
	{
		get => _totalMessagesReceived;
		private set => this.RaiseAndSetIfChanged(ref _totalMessagesReceived, value);
	}

	/// <summary>
	/// 最後にメッセージを受信した時刻
	/// </summary>
	public DateTime? LastMessageTime =>
		_directController?.LastMessageTime ?? RedundantController?.LastMessageTime;

	/// <summary>
	/// データ受信イベント
	/// </summary>
	public event EventHandler<DataWebSocketMessage>? DataReceived;

	/// <summary>
	/// 接続状態変更イベント
	/// </summary>
	public event EventHandler? ConnectionStatusChanged;

	/// <summary>
	/// WebSocket の ping 関連表示値が更新された
	/// </summary>
	public event EventHandler? PingStatisticsChanged;

	/// <summary>
	/// すべての接続失効イベント
	/// </summary>
	public event EventHandler? AllConnectionsLost;

	public DmdataConnectionManager(ILogManager logManager)
	{
		Logger = logManager.GetLogger<DmdataConnectionManager>();
	}

	/// <summary>
	/// WebSocket接続を開始する。
	/// customDefaultEndpoint が ws:// / wss:// で始まる場合は直接接続モードになる。
	/// </summary>
	public async Task ConnectWebSocketAsync(
		IDmdataV2ApiClient? apiClient,
		IEnumerable<InformationCategory> subscribingCategories,
		string appVersion,
		bool receiveTraining,
		bool useRedundancy,
		string[] customRedundantEndpoints,
		string? customDefaultEndpoint)
	{
		// 直接接続モードの判定: 単一接続 かつ URL が ws:// / wss:// で始まる場合
		var isDirectUrl = !useRedundancy &&
			customDefaultEndpoint?.StartsWith("ws", StringComparison.OrdinalIgnoreCase) == true;

		if (isDirectUrl)
		{
			await ConnectDirectAsync(customDefaultEndpoint!);
			return;
		}

		if (apiClient == null)
			throw new InvalidOperationException("通常接続にはApiClientが必要です");

		Logger.LogDebug("WebSocket接続処理を開始します");
		IsDirectMode = false;
		_lastRedundantPingIntervalSeconds = null;
		_previousRedundantPingAt = null;

		RedundantController?.Dispose();
		RedundantController = new RedundantDmdataSocketController(apiClient);
		RedundantController.Options.EnableRawDataEvents = true;
		ConfigureRedundantControllerEvents();

		var classifications = subscribingCategories.Select(c => TelegramCategoryMap[c]).Distinct().ToArray();
		if (classifications.Length <= 0)
		{
			Logger.LogInfo("取得対象が存在しないため接続しません");
			throw new InvalidOperationException("取得対象が存在しません");
		}

		var parameter = new SocketStartRequestParameter(classifications)
		{
			AppName = $"KEVi v{appVersion}",
			Types = subscribingCategories
				.Where(c => DmdataDataProcessor.GetTypesFromCategory(c).Length > 0)
				.SelectMany(c => DmdataDataProcessor.GetTypesFromCategory(c))
				.ToArray(),
			Test = receiveTraining ? "including" : "no",
		};

		// 冗長性設定に基づいてエンドポイントを選択
		string[] endpoints;
		if (useRedundancy)
		{
			// 冗長接続モード: カスタムエンドポイントまたはデフォルトの冗長エンドポイントを使用
			if (customRedundantEndpoints.Length > 0)
				endpoints = customRedundantEndpoints;
			else
				endpoints = RedundantSocketOptions.DefaultEndpoints;
		}
		else
		{
			// 単一接続モード: カスタムエンドポイントまたはグローバルエンドポイントを使用
			if (!string.IsNullOrWhiteSpace(customDefaultEndpoint))
				endpoints = [customDefaultEndpoint];
			else
				endpoints = [DmdataV2SocketEndpoints.Global];
		}

		await RedundantController.ConnectAsync(parameter, endpoints);
		UpdateConnectionStatus();
	}

	/// <summary>
	/// 直接URLへのWebSocket接続を開始する（認証・Ticket発行なし）
	/// </summary>
	private async Task ConnectDirectAsync(string url)
	{
		Logger.LogInfo($"直接接続モードで WebSocket 接続します: {url}");
		IsDirectMode = true;
		_lastRedundantPingIntervalSeconds = null;
		_previousRedundantPingAt = null;

		RedundantController?.Dispose();
		RedundantController = null;

		_directController?.Dispose();
		_directController = new DirectWebSocketController(Locator.Current.GetService<ILogManager>()!);
		ConfigureDirectControllerEvents();

		await _directController.ConnectAsync(url);
		UpdateConnectionStatus();
	}

	/// <summary>
	/// WebSocket接続を切断する
	/// </summary>
	public async Task DisconnectWebSocketAsync()
	{
		if (_directController != null)
		{
			await _directController.DisconnectAsync();
			_directController.Dispose();
			_directController = null;
			IsDirectMode = false;
			UpdateConnectionStatus();
			NotifyPingStatisticsChanged();
			return;
		}

		if (RedundantController != null)
		{
			await RedundantController.DisconnectAsync();
			_lastRedundantPingIntervalSeconds = null;
			_previousRedundantPingAt = null;
			UpdateConnectionStatus();
			NotifyPingStatisticsChanged();
		}
	}

	/// <summary>
	/// DirectWebSocketControllerのイベントハンドラを設定する
	/// </summary>
	private void ConfigureDirectControllerEvents()
	{
		if (_directController == null)
			return;

		_directController.PingLatencyUpdated += (_, _) => NotifyPingStatisticsChanged();

		_directController.DataReceived += (s, e) =>
		{
			TotalMessagesReceived++;
			DataReceived?.Invoke(this, e);
		};

		_directController.Connected += (s, e) =>
		{
			Logger.LogInfo("直接WebSocket接続が確立されました");
			UpdateConnectionStatus();
			ConnectionStatusChanged?.Invoke(this, EventArgs.Empty);
		};

		_directController.Disconnected += (s, e) =>
		{
			Logger.LogWarning("直接WebSocket接続が切断されました");
			UpdateConnectionStatus();
			AllConnectionsLost?.Invoke(this, EventArgs.Empty);
		};
	}

	/// <summary>
	/// RedundantControllerのイベントハンドラを設定する
	/// </summary>
	private void ConfigureRedundantControllerEvents()
	{
		if (RedundantController == null)
			return;

		RedundantController.DataReceived += (s, e) =>
		{
			DataReceived?.Invoke(this, e);
		};

		RedundantController.RawDataReceived += (s, e) =>
		{
			TotalMessagesReceived = RedundantController.TotalMessagesReceived;
			TryRecordRedundantPingInterval(e);
		};

		RedundantController.AllConnectionsLost += (s, e) =>
		{
			Logger.LogWarning("すべての接続が失われました");
			UpdateConnectionStatus();
			AllConnectionsLost?.Invoke(this, EventArgs.Empty);
		};

		RedundantController.RedundancyRestored += (s, e) =>
		{
			Logger.LogInfo($"冗長性が復旧しました エンドポイント:{e.RestoredEndpoint} アクティブ接続数:{e.TotalActiveConnections}");
			UpdateConnectionStatus();
			ConnectionStatusChanged?.Invoke(this, EventArgs.Empty);
		};

		RedundantController.ConnectionError += (s, e) =>
		{
			var errorDetail = e.ErrorMessage != null
				? $"コード:{e.ErrorMessage.Code} メッセージ:{e.ErrorMessage.Error}"
				: e.Exception?.Message;
			Logger.LogWarning($"接続エラーが発生しました エンドポイント:{e.EndpointName} エラー:{errorDetail}");
			UpdateConnectionStatus();
			ConnectionStatusChanged?.Invoke(this, EventArgs.Empty);
		};

		RedundantController.ConnectionEstablished += (s, e) =>
		{
			UpdateConnectionStatus();
			ConnectionStatusChanged?.Invoke(this, EventArgs.Empty);
		};
	}

	private void TryRecordRedundantPingInterval(RawDataReceivedEventArgs e)
	{
		if (e.IsDuplicate || !IsWebSocketPingJson(e.Message))
			return;

		if (_previousRedundantPingAt is { } prev)
			_lastRedundantPingIntervalSeconds = (e.ReceivedTime - prev).TotalSeconds;
		_previousRedundantPingAt = e.ReceivedTime;
		NotifyPingStatisticsChanged();
	}

	private static bool IsWebSocketPingJson(string json)
	{
		try
		{
			using var doc = JsonDocument.Parse(json);
			return doc.RootElement.TryGetProperty("type", out var t) && t.GetString() == "ping";
		}
		catch
		{
			return false;
		}
	}

	private void NotifyPingStatisticsChanged()
	{
		this.RaisePropertyChanged(nameof(LastPongSendMilliseconds));
		this.RaisePropertyChanged(nameof(LastPingIntervalSeconds));
		PingStatisticsChanged?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>
	/// 接続状態を更新する
	/// </summary>
	private void UpdateConnectionStatus()
	{
		if (_directController != null)
		{
			RedundancyStatus = _directController.IsConnected
				? RedundancyStatus.FullyConnected
				: RedundancyStatus.Disconnected;
			ActiveConnectionCount = _directController.IsConnected ? 1 : 0;
			ConnectedEndpoints = _directController.IsConnected && _directController.EndpointUrl != null
				? [_directController.EndpointUrl]
				: [];
			TotalMessagesReceived = _directController.TotalMessagesReceived;
		}
		else if (RedundantController != null)
		{
			RedundancyStatus = RedundantController.Status;
			ActiveConnectionCount = RedundantController.ActiveConnectionCount;
			ConnectedEndpoints = RedundantController.ConnectedEndpoints ?? [];
			TotalMessagesReceived = RedundantController.TotalMessagesReceived;
		}
		else
		{
			RedundancyStatus = RedundancyStatus.Disconnected;
			ActiveConnectionCount = 0;
			ConnectedEndpoints = [];
			TotalMessagesReceived = 0;
		}
	}

	/// <summary>
	/// WebSocketが接続されているかどうか
	/// </summary>
	public bool IsWebSocketConnected =>
		_directController?.IsConnected ?? RedundantController?.IsConnected ?? false;

	public void Dispose()
	{
		_directController?.Dispose();
		RedundantController?.Dispose();
		GC.SuppressFinalize(this);
	}
}
