using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Services.EqMonitor;

/// <summary>
/// EQMonitor WebSocket から受信した型付きメッセージ
/// </summary>
public abstract record EqMonitorRealtimeMessage;

public sealed record EqMonitorReadyMessage : EqMonitorRealtimeMessage;

public sealed record EqMonitorPingMessage : EqMonitorRealtimeMessage;

public sealed record EqMonitorPongMessage(string? PingId) : EqMonitorRealtimeMessage;

public sealed record EqMonitorEewUpsertMessage(Generated.EewItemWithRelations Record)
	: EqMonitorRealtimeMessage;

public sealed record EqMonitorEarthquakeUpsertMessage(Generated.Earthquake Record)
	: EqMonitorRealtimeMessage;

public sealed record EqMonitorEarthquakeDeleteMessage(string EventId)
	: EqMonitorRealtimeMessage;

public sealed record EqMonitorUnsupportedMessage(string Type, string? Operation)
	: EqMonitorRealtimeMessage;
