using System;

namespace ReplayGenerator.Models;

public class EewState
{
	public string EventId { get; set; } = "";
	public DateTime? OriginTime { get; set; }
	public DateTime ReportTime { get; set; }
	public double? Magnitude { get; set; }
	public double? DepthKm { get; set; }
}
