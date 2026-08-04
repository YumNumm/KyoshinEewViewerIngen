using Avalonia;
using Avalonia.iOS;
using Foundation;
using KyoshinEewViewer.Core;
using ReactiveUI.Avalonia;

namespace KyoshinEewViewer.iOS;

[Register("AppDelegate")]
public partial class AppDelegate : AvaloniaAppDelegate<App>
{
	protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
		=> base.CustomizeAppBuilder(builder)
			.UseKeviFonts()
			.UseReactiveUI(_ => { });

}
