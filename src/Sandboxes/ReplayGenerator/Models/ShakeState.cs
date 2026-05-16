using System;

namespace ReplayGenerator.Models;

public class ShakeState
{
	public string ShakeEventId { get; set; } = "";
	public DateTime StartTime { get; set; }
	public DateTime LastEventTime { get; set; }
	public string? EewJson { get; set; }
	public SessionStatus Status { get; set; } = SessionStatus.Tracking;

	public string? AssociatedEewEventId { get; set; }
	public DateTime? AssociatedEewOriginTime { get; set; }
	public DateTime? AssociatedEewReportTime { get; set; }
	public double? AssociatedEewMagnitude { get; set; }
	public double? AssociatedEewDepthKm { get; set; }
}
