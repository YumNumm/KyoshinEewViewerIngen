using Avalonia.Controls;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Series;
using Splat;

namespace KyoshinEewViewer.Services.EqMonitor;

public class EqMonitorSettingPage : ISettingPage
{
	public bool IsVisible => true;

	public string? Icon => null;

	public string Title => "EQMonitor(試験中)";

	public Control DisplayControl => new EqMonitorPage() { DataContext = this };

	public ISettingPage[] SubPages => [];

	public KyoshinEewViewerConfiguration Config { get; }

	public EqMonitorSettingPage(KyoshinEewViewerConfiguration config)
	{
		SplatRegistrations.RegisterLazySingleton<EqMonitorSettingPage>();

		Config = config;
	}
}
