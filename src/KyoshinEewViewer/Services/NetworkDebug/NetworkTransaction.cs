using System;
using System.Collections.Generic;

namespace KyoshinEewViewer.Services.NetworkDebug;

/// <summary>
/// 記録した通信の種別
/// </summary>
public enum NetworkTransactionKind
{
	Http,
	WebSocket,
}

/// <summary>
/// WebSocket の通信方向
/// </summary>
public enum WebSocketDirection
{
	/// <summary>接続</summary>
	Connect,
	/// <summary>切断</summary>
	Disconnect,
	/// <summary>送信</summary>
	Send,
	/// <summary>受信</summary>
	Receive,
}

/// <summary>
/// 記録した通信 1 件
/// </summary>
public abstract class NetworkTransaction
{
	/// <summary>
	/// 記録順の連番。<see cref="NetworkDebugRecorder.Record"/> が採番する
	/// </summary>
	public long Id { get; internal set; }

	/// <summary>
	/// 通信を開始した時刻
	/// </summary>
	public DateTime Timestamp { get; init; } = DateTime.Now;

	public abstract NetworkTransactionKind Kind { get; }

	/// <summary>
	/// 失敗した場合のメッセージ
	/// </summary>
	public string? Error { get; init; }
}

/// <summary>
/// HTTP 通信の記録
/// </summary>
public class HttpTransaction : NetworkTransaction
{
	public override NetworkTransactionKind Kind => NetworkTransactionKind.Http;

	public required string Method { get; init; }
	public required Uri Uri { get; init; }

	public IReadOnlyList<KeyValuePair<string, string>> RequestHeaders { get; init; } = [];
	public string? RequestBody { get; init; }

	public int? StatusCode { get; init; }
	public IReadOnlyList<KeyValuePair<string, string>> ResponseHeaders { get; init; } = [];
	public string? ResponseBody { get; init; }

	/// <summary>
	/// レスポンスの Content-Type
	/// </summary>
	public string? ContentType { get; init; }

	/// <summary>
	/// レスポンスのバイト数。取得できなかった場合は null
	/// </summary>
	public long? ContentLength { get; init; }

	public TimeSpan Duration { get; init; }
}

/// <summary>
/// WebSocket 通信の記録
/// </summary>
public class WebSocketTransaction : NetworkTransaction
{
	public override NetworkTransactionKind Kind => NetworkTransactionKind.WebSocket;

	/// <summary>
	/// 接続先。認証情報を含みうるクエリは <see cref="NetworkDebugRecorder.RecordWebSocket"/> が除去する
	/// </summary>
	public required string Endpoint { get; init; }

	public required WebSocketDirection Direction { get; init; }

	/// <summary>
	/// フレームの内容。バイナリフレームや取得を見送った場合は null
	/// </summary>
	public string? Payload { get; init; }

	/// <summary>
	/// フレームのバイト数
	/// </summary>
	public long PayloadLength { get; init; }
}

/// <summary>
/// 通信記録の追加イベント
/// </summary>
public class NetworkTransactionAdded
{
	public required NetworkTransaction Transaction { get; init; }
}
