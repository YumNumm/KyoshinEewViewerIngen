using KyoshinEewViewer.Core.Models;

namespace KyoshinEewViewer.Series.KyoshinMonitor.Events;

public class KyoshinShakeDetected(KyoshinEvent @event, bool isLevelUp, bool isReplay)
{
	public KyoshinEvent Event { get; } = @event;
	public bool IsLevelUp { get; } = isLevelUp;
	public bool IsReplay { get; } = isReplay;
}
