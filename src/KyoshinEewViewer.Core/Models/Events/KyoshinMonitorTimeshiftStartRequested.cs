using ReactiveUI;

namespace KyoshinEewViewer.Core.Models.Events;

public record KyoshinMonitorTimeshiftStartRequested(int TimeshiftSeconds)
{
	public static void Request(int timeshiftSeconds)
		=> MessageBus.Current.SendMessage(new KyoshinMonitorTimeshiftStartRequested(timeshiftSeconds));

}

