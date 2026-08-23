using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Core;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace KyoshinEewViewer.Services.NetworkDebug;

/// <summary>
/// 通信内容をメモリ上に保持する<br/>
/// 既定では無効で、設定から明示的に有効化された場合のみ記録する
/// </summary>
public class NetworkDebugRecorder
{
	/// <summary>
	/// 保持する件数の上限
	/// </summary>
	public const int MaxEntryCount = 500;

	/// <summary>
	/// 記録するボディの最大文字数
	/// </summary>
	public const int MaxBodyLength = 256 * 1024;

	private readonly ConcurrentQueue<NetworkTransaction> _transactions = new();
	private long _lastId;

	private KyoshinEewViewerConfiguration Config { get; }

	public NetworkDebugRecorder(KyoshinEewViewerConfiguration config)
	{
		Config = config;
	}

	/// <summary>
	/// 通信内容を記録するか
	/// </summary>
	public bool IsEnabled => Config.Debug.RecordNetworkTraffic;

	/// <summary>
	/// 通信内容を記録する。無効な場合は何もしない
	/// </summary>
	public void Record(NetworkTransaction transaction)
	{
		if (!IsEnabled)
			return;

		transaction.Id = Interlocked.Increment(ref _lastId);
		_transactions.Enqueue(transaction);

		while (_transactions.Count > MaxEntryCount)
			_transactions.TryDequeue(out _);

		StrongReferenceMessenger.Default.Send(new NetworkTransactionAdded { Transaction = transaction });
	}

	/// <summary>
	/// 保持しているすべての記録を取得する
	/// </summary>
	public IReadOnlyList<NetworkTransaction> GetAll() => _transactions.ToArray();

	/// <summary>
	/// 記録を破棄する
	/// </summary>
	public void Clear() => _transactions.Clear();

	private static NetworkDebugRecorder? _current;

	/// <summary>
	/// 静的フィールドで生成される HttpClient があるため、DI の準備完了を待って遅延解決する
	/// </summary>
	public static NetworkDebugRecorder? Current => _current ??= ServiceLocator.CurrentOrNull?.GetService<NetworkDebugRecorder>();

	/// <summary>
	/// WebSocket の通信を記録する<br/>
	/// 記録に失敗しても通信本体へは影響させない
	/// </summary>
	public static void RecordWebSocket(string endpoint, WebSocketDirection direction, string? payload = null, long payloadLength = 0, string? error = null)
	{
		try
		{
			var recorder = Current;
			if (recorder is null || !recorder.IsEnabled)
				return;

			// 途中で切ると認証情報が残る可能性があるため、伏せ字を適用してから切り詰める
			var masked = payload is null ? null : NetworkCaptureMasker.MaskBody(payload);
			if (masked is not null && masked.Length > MaxBodyLength)
				masked = masked[..MaxBodyLength] + $"…(以下 {masked.Length - MaxBodyLength} 文字を省略)";

			recorder.Record(new WebSocketTransaction
			{
				Endpoint = NetworkCaptureMasker.SanitizeEndpoint(endpoint),
				Direction = direction,
				Payload = masked,
				PayloadLength = payloadLength,
				Error = error,
			});
		}
		catch (Exception ex)
		{
			AppLog.Default.LogWarning(ex, "WebSocket 通信の記録に失敗しました");
		}
	}
}
