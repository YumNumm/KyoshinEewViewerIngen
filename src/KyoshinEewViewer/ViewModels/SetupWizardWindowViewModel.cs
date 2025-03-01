using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Series.Earthquake;
using KyoshinEewViewer.Series.KyoshinMonitor;
using KyoshinEewViewer.Series.Tsunami;
using Splat;

namespace KyoshinEewViewer.ViewModels;
public class SetupWizardWindowViewModel : ViewModelBase
{
	public KyoshinEewViewerConfiguration Config { get; }

	public bool IsKyoshinMonitorEnabled
	{
		get => Config.SeriesEnable.TryGetValue(KyoshinMonitorSeries.MetaData.Key, out var e) ? e : KyoshinMonitorSeries.MetaData.IsDefaultEnabled;
		set => Config.SeriesEnable[KyoshinMonitorSeries.MetaData.Key] = value;
	}
	public bool IsEarthquakeEnabled
	{
		get => Config.SeriesEnable.TryGetValue(EarthquakeSeries.MetaData.Key, out var e) ? e : EarthquakeSeries.MetaData.IsDefaultEnabled;
		set => Config.SeriesEnable[EarthquakeSeries.MetaData.Key] = value;
	}
	public bool IsTsunamiEnabled
	{
		get => Config.SeriesEnable.TryGetValue(TsunamiSeries.MetaData.Key, out var e) ? e : TsunamiSeries.MetaData.IsDefaultEnabled;
		set => Config.SeriesEnable[TsunamiSeries.MetaData.Key] = value;
	}

	public SetupWizardWindowViewModel(KyoshinEewViewerConfiguration config)
	{
		SplatRegistrations.RegisterLazySingleton<SetupWizardWindowViewModel>();

		Config = config;
	}
}
