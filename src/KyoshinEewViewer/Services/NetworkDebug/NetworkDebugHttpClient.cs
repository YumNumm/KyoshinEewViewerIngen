using System;
using System.Net.Http;

namespace KyoshinEewViewer.Services.NetworkDebug;

/// <summary>
/// 通信内容の記録に対応した <see cref="HttpClient"/> を生成する<br/>
/// アプリ内で <see cref="HttpClient"/> を生成する箇所はすべてここを経由させる
/// </summary>
public static class NetworkDebugHttpClient
{
	/// <summary>
	/// 通信内容の記録に対応した <see cref="HttpClient"/> を生成する
	/// </summary>
	/// <param name="innerHandler">実際に通信を行うハンドラ。省略時は既定のハンドラを使用する</param>
	/// <param name="timeout">タイムアウト。省略時は <see cref="HttpClient"/> の既定値を使用する</param>
	public static HttpClient Create(HttpMessageHandler? innerHandler = null, TimeSpan? timeout = null)
	{
		var client = new HttpClient(new NetworkCaptureHandler(innerHandler ?? new HttpClientHandler()));
		if (timeout is { } value)
			client.Timeout = value;
		return client;
	}
}
