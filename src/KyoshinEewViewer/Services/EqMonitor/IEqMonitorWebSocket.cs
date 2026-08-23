using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KyoshinEewViewer.Services.EqMonitor;

/// <summary>
/// EQMonitor のリアルタイム接続で使用する WebSocket 抽象
/// </summary>
internal interface IEqMonitorWebSocket : IAsyncDisposable
{
	Task ConnectAsync(
		Uri endpoint,
		IReadOnlyDictionary<string, string> headers,
		CancellationToken cancellationToken);
	Task<string?> ReceiveTextAsync(CancellationToken cancellationToken);
	Task SendTextAsync(string text, CancellationToken cancellationToken);
	Task CloseAsync(CancellationToken cancellationToken);
}
