using KyoshinEewViewer.Services.NetworkDebug;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KyoshinEewViewer.Services.EqMonitor;

/// <summary>
/// <see cref="ClientWebSocket"/> を使う実運用向けアダプター
/// </summary>
internal sealed class EqMonitorClientWebSocket : IEqMonitorWebSocket
{
	private const int ReceiveBufferSize = 8192;
	private const int MaxMessageLength = 4 * 1024 * 1024;

	private ClientWebSocket Socket { get; } = new();
	private string? Endpoint { get; set; }
	private bool IsDisconnectRecorded { get; set; }

	public async Task ConnectAsync(
		Uri endpoint,
		IReadOnlyDictionary<string, string> headers,
		CancellationToken cancellationToken)
	{
		Endpoint = endpoint.AbsoluteUri;
		foreach (var header in headers)
			Socket.Options.SetRequestHeader(header.Key, header.Value);
		try
		{
			await Socket.ConnectAsync(endpoint, cancellationToken);
			NetworkDebugRecorder.RecordWebSocket(Endpoint, WebSocketDirection.Connect);
		}
		catch (Exception ex)
		{
			NetworkDebugRecorder.RecordWebSocket(
				Endpoint,
				WebSocketDirection.Connect,
				error: ex.Message);
			throw;
		}
	}

	public async Task<string?> ReceiveTextAsync(CancellationToken cancellationToken)
	{
		var buffer = new byte[ReceiveBufferSize];
		using var stream = new MemoryStream();
		while (true)
		{
			var result = await Socket.ReceiveAsync(buffer, cancellationToken);
			if (result.MessageType == WebSocketMessageType.Close)
				return null;
			if (result.MessageType != WebSocketMessageType.Text)
				throw new WebSocketException("EQMonitor WebSocket からバイナリメッセージを受信しました");

			stream.Write(buffer, 0, result.Count);
			if (stream.Length > MaxMessageLength)
				throw new WebSocketException("EQMonitor WebSocket のメッセージが大きすぎます");
			if (!result.EndOfMessage)
				continue;

			var text = Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
			NetworkDebugRecorder.RecordWebSocket(
				Endpoint!,
				WebSocketDirection.Receive,
				text,
				stream.Length);
			return text;
		}
	}

	public async Task SendTextAsync(string text, CancellationToken cancellationToken)
	{
		var bytes = Encoding.UTF8.GetBytes(text);
		try
		{
			await Socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
			NetworkDebugRecorder.RecordWebSocket(
				Endpoint!,
				WebSocketDirection.Send,
				text,
				bytes.Length);
		}
		catch (Exception ex)
		{
			NetworkDebugRecorder.RecordWebSocket(
				Endpoint!,
				WebSocketDirection.Send,
				text,
				bytes.Length,
				ex.Message);
			throw;
		}
	}

	public async Task CloseAsync(CancellationToken cancellationToken)
	{
		try
		{
			if (Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
				await Socket.CloseOutputAsync(
					WebSocketCloseStatus.NormalClosure,
					"アプリケーションによる切断",
					cancellationToken);
		}
		finally
		{
			RecordDisconnect();
		}
	}

	private void RecordDisconnect()
	{
		if (Endpoint == null || IsDisconnectRecorded)
			return;
		IsDisconnectRecorded = true;
		NetworkDebugRecorder.RecordWebSocket(Endpoint, WebSocketDirection.Disconnect);
	}

	public ValueTask DisposeAsync()
	{
		RecordDisconnect();
		Socket.Dispose();
		return ValueTask.CompletedTask;
	}
}
