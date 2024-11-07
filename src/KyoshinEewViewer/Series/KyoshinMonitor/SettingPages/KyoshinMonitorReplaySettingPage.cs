using Avalonia.Controls;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Core.Models.Events;
using ReactiveUI;
using System.Text;
using System;
using System.Reactive;

namespace KyoshinEewViewer.Series.KyoshinMonitor.SettingPages;

public class KyoshinMonitorReplaySettingPage : ReactiveObject, ISettingPage
{
	public bool IsVisible => true;

	public string? Icon => null;

	public string Title => "リプレイ";

	public Control DisplayControl => new KyoshinMonitorReplayPage() { DataContext = this };

	public ISettingPage[] SubPages => [];

	public KyoshinMonitorSeries Series { get; }
	private KyoshinEewViewerConfiguration Config { get; }


	private int _timeshiftSeconds = 0;
	public int TimeshiftSeconds
	{
		get => _timeshiftSeconds;
		set {
			if (value > 10800)
				value = 10800;
			if (value < 0)
				value = 0;
			this.RaiseAndSetIfChanged(ref _timeshiftSeconds, value);
			UpdateTimeshiftString();
		}
	}
	private string _timeshiftSecondsString = "リアルタイム";

	public KyoshinMonitorReplaySettingPage(KyoshinEewViewerConfiguration config, KyoshinMonitorSeries series)
    {
		Series = series;
		Config = config;

		OffsetTimeshiftSeconds = ReactiveCommand.Create<string>(amountString =>
		{
			var amount = int.Parse(amountString);
			TimeshiftSeconds += amount;
		});
	}

	public string TimeshiftSecondsString
	{
		get => _timeshiftSecondsString;
		set => this.RaiseAndSetIfChanged(ref _timeshiftSecondsString, value);
	}
	private void UpdateTimeshiftString()
	{
		if (TimeshiftSeconds == 0)
		{
			TimeshiftSecondsString = "リアルタイム";
			return;
		}

		var sb = new StringBuilder();
		var time = TimeSpan.FromSeconds(TimeshiftSeconds);
		if (time.TotalHours >= 1)
			sb.Append((int)time.TotalHours + "時間");
		if (time.Minutes > 0)
			sb.Append(time.Minutes + "分");
		if (time.Seconds > 0)
			sb.Append(time.Seconds + "秒");
		sb.Append('前');

		TimeshiftSecondsString = sb.ToString();
	}

	public ReactiveCommand<string, Unit> OffsetTimeshiftSeconds { get; }
	public void StartTimeshift()
		=> KyoshinMonitorTimeshiftStartRequested.Request(TimeshiftSeconds);
	public void ReturnToRealtime()
		=> KyoshinMonitorReplayStopRequest.Request();
}
