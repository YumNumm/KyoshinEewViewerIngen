using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace KyoshinEewViewer.Series.KyoshinMonitor.SettingPages;
public partial class KyoshinMonitorEewPage : UserControl
{
	public KyoshinMonitorEewPage()
	{
		InitializeComponent();
	}

	private void InitializeComponent()
	{
		AvaloniaXamlLoader.Load(this);
	}
}
