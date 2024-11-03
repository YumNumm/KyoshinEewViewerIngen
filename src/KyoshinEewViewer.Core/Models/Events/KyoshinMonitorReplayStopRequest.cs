using ReactiveUI;

namespace KyoshinEewViewer.Core.Models.Events;

public class KyoshinMonitorReplayStopRequest
{
	public static void Request()
		=> MessageBus.Current.SendMessage(new KyoshinMonitorReplayStopRequest());
}
