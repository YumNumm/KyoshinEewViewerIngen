using KyoshinEewViewer.Services.NetworkDebug;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KyoshinEewViewer.ViewModels;

/// <summary>
/// デバッグウィンドウの通信タブに 1 行として表示する記録
/// </summary>
public class NetworkTransactionViewModel
{
	public required string Time { get; init; }

	/// <summary>HTTP / WS</summary>
	public required string Kind { get; init; }

	/// <summary>HTTP はメソッド、WebSocket は通信方向</summary>
	public required string Operation { get; init; }

	public required string Host { get; init; }
	public required string Path { get; init; }
	public required string Status { get; init; }
	public required string Duration { get; init; }
	public required string Size { get; init; }

	public required string RequestDetail { get; init; }
	public required string ResponseDetail { get; init; }

	public static NetworkTransactionViewModel From(NetworkTransaction transaction) => transaction switch
	{
		HttpTransaction http => FromHttp(http),
		WebSocketTransaction webSocket => FromWebSocket(webSocket),
		_ => throw new ArgumentOutOfRangeException(nameof(transaction), transaction, "未対応の通信種別です"),
	};

	private static NetworkTransactionViewModel FromHttp(HttpTransaction t) => new()
	{
		Time = t.Timestamp.ToString("HH:mm:ss.fff"),
		Kind = "HTTP",
		Operation = t.Method,
		Host = t.Uri.Host,
		Path = t.Uri.PathAndQuery,
		Status = t.Error is not null ? "失敗" : t.StatusCode?.ToString() ?? "-",
		Duration = $"{t.Duration.TotalMilliseconds:F0} ms",
		Size = FormatSize(t.ContentLength),
		RequestDetail = BuildDetail(t.RequestHeaders, t.RequestBody, t.ContentType),
		ResponseDetail = t.Error is not null
			? $"エラー: {t.Error}"
			: BuildDetail(t.ResponseHeaders, t.ResponseBody, t.ContentType),
	};

	private static NetworkTransactionViewModel FromWebSocket(WebSocketTransaction t)
	{
		var operation = t.Direction switch
		{
			WebSocketDirection.Connect => "接続",
			WebSocketDirection.Disconnect => "切断",
			WebSocketDirection.Send => "送信",
			WebSocketDirection.Receive => "受信",
			_ => t.Direction.ToString(),
		};

		// Endpoint は wss://host/path 形式だが、記録に失敗した場合に備えて解析できないケースも許容する
		var host = Uri.TryCreate(t.Endpoint, UriKind.Absolute, out var uri) ? uri.Host : t.Endpoint;
		var path = uri?.AbsolutePath ?? "";

		return new NetworkTransactionViewModel
		{
			Time = t.Timestamp.ToString("HH:mm:ss.fff"),
			Kind = "WS",
			Operation = operation,
			Host = host,
			Path = path,
			Status = t.Error is not null ? "失敗" : "-",
			Duration = "-",
			Size = t.PayloadLength > 0 ? FormatSize(t.PayloadLength) : "-",
			RequestDetail = t.Direction == WebSocketDirection.Send ? t.Payload ?? "(本文なし)" : "",
			ResponseDetail = t.Error is not null
				? $"エラー: {t.Error}"
				: t.Direction == WebSocketDirection.Send ? "" : t.Payload ?? "(本文なし)",
		};
	}

	private static string BuildDetail(IReadOnlyList<KeyValuePair<string, string>> headers, string? body, string? contentType)
	{
		var headerText = headers.Count == 0
			? "(ヘッダなし)"
			: string.Join(Environment.NewLine, headers.Select(h => $"{h.Key}: {h.Value}"));

		var bodyText = body ?? (contentType is null
			? "(本文なし)"
			: $"(本文は記録対象外の形式です: {contentType})");

		return $"{headerText}{Environment.NewLine}{Environment.NewLine}{bodyText}";
	}

	private static string FormatSize(long? bytes) => bytes switch
	{
		null => "-",
		< 1024 => $"{bytes} B",
		< 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
		_ => $"{bytes / (1024.0 * 1024.0):F1} MB",
	};
}
