using KyoshinEewViewer.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace KyoshinEewViewer.Services.NetworkDebug;

/// <summary>
/// HTTP 通信の内容を <see cref="NetworkDebugRecorder"/> へ記録する<br/>
/// 記録処理で発生した例外は握りつぶし、通信本体には一切影響させない
/// </summary>
/// <param name="innerHandler">実際に通信を行うハンドラ</param>
/// <param name="recorder">記録先。省略時は DI から解決する</param>
public class NetworkCaptureHandler(HttpMessageHandler innerHandler, NetworkDebugRecorder? recorder = null) : DelegatingHandler(innerHandler)
{
	/// <summary>
	/// ボディを記録する Content-Type<br/>
	/// 画像やアーカイブを記録対象から外すことで、強震モニタの画像取得による肥大化を防ぐ
	/// </summary>
	private static readonly string[] TextMediaTypes = [
		"application/json",
		"application/xml",
		"application/problem+json",
		"application/javascript",
		"application/x-www-form-urlencoded",
	];

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var target = recorder ?? NetworkDebugRecorder.Current;
		if (target is null || !target.IsEnabled)
			return await base.SendAsync(request, cancellationToken);

		var captured = await CaptureRequestAsync(request, cancellationToken);
		var stopwatch = Stopwatch.StartNew();
		try
		{
			var response = await base.SendAsync(request, cancellationToken);
			stopwatch.Stop();
			await RecordResponseAsync(target, captured, response, stopwatch.Elapsed, cancellationToken);
			return response;
		}
		catch (Exception ex)
		{
			stopwatch.Stop();
			RecordFailure(target, captured, ex, stopwatch.Elapsed);
			throw;
		}
	}

	/// <summary>
	/// 送信前のリクエスト情報を控える
	/// </summary>
	private static async Task<CapturedRequest> CaptureRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var timestamp = DateTime.Now;
		var method = request.Method.Method;
		var uri = request.RequestUri ?? new Uri("about:blank");
		List<KeyValuePair<string, string>> headers = [];
		string? body = null;

		try
		{
			headers = NetworkCaptureMasker.MaskHeaders(request.Headers);
			if (request.Content is { } content)
			{
				headers.AddRange(NetworkCaptureMasker.MaskHeaders(content.Headers));
				body = await ReadBodyAsync(content, cancellationToken);
			}
		}
		catch (Exception ex)
		{
			AppLog.Default.LogWarning(ex, "リクエスト内容の記録に失敗しました");
		}

		return new CapturedRequest(timestamp, method, uri, headers, body);
	}

	private static async Task RecordResponseAsync(NetworkDebugRecorder recorder, CapturedRequest captured, HttpResponseMessage response, TimeSpan duration, CancellationToken cancellationToken)
	{
		try
		{
			var headers = NetworkCaptureMasker.MaskHeaders(response.Headers);
			headers.AddRange(NetworkCaptureMasker.MaskHeaders(response.Content.Headers));

			// ボディの読み込みでバッファされると Content-Length が確定するため、長さは読み込み後に取得する
			var body = await ReadBodyAsync(response.Content, cancellationToken);

			recorder.Record(new HttpTransaction
			{
				Timestamp = captured.Timestamp,
				Method = captured.Method,
				Uri = captured.Uri,
				RequestHeaders = captured.Headers,
				RequestBody = captured.Body,
				StatusCode = (int)response.StatusCode,
				ResponseHeaders = headers,
				ResponseBody = body,
				ContentType = response.Content.Headers.ContentType?.MediaType,
				ContentLength = response.Content.Headers.ContentLength,
				Duration = duration,
			});
		}
		catch (Exception ex)
		{
			AppLog.Default.LogWarning(ex, "レスポンス内容の記録に失敗しました");
		}
	}

	private static void RecordFailure(NetworkDebugRecorder recorder, CapturedRequest captured, Exception exception, TimeSpan duration)
	{
		try
		{
			recorder.Record(new HttpTransaction
			{
				Timestamp = captured.Timestamp,
				Method = captured.Method,
				Uri = captured.Uri,
				RequestHeaders = captured.Headers,
				RequestBody = captured.Body,
				Duration = duration,
				Error = exception.Message,
			});
		}
		catch (Exception ex)
		{
			AppLog.Default.LogWarning(ex, "通信失敗の記録に失敗しました");
		}
	}

	/// <summary>
	/// ボディを読み取る。テキストとして扱えない Content-Type の場合は読み取らない
	/// </summary>
	private static async Task<string?> ReadBodyAsync(HttpContent content, CancellationToken cancellationToken)
	{
		if (!IsTextMediaType(content.Headers.ContentType?.MediaType))
			return null;

		// バッファへ読み込んでおくことで、記録後も呼び出し元が改めてボディを読める
		await content.LoadIntoBufferAsync();
		var body = await content.ReadAsStringAsync(cancellationToken);

		// 途中で切ると認証情報が残る可能性があるため、伏せ字を適用してから切り詰める
		return Truncate(NetworkCaptureMasker.MaskBody(body));
	}

	private static bool IsTextMediaType(string? mediaType)
	{
		if (string.IsNullOrEmpty(mediaType))
			return false;
		if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
			return true;
		if (mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase) ||
			mediaType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase))
			return true;
		return TextMediaTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase);
	}

	private static string Truncate(string body)
		=> body.Length <= NetworkDebugRecorder.MaxBodyLength
			? body
			: body[..NetworkDebugRecorder.MaxBodyLength] + $"…(以下 {body.Length - NetworkDebugRecorder.MaxBodyLength} 文字を省略)";

	private sealed record CapturedRequest(
		DateTime Timestamp,
		string Method,
		Uri Uri,
		List<KeyValuePair<string, string>> Headers,
		string? Body);
}
