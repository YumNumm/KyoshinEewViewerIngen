using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.iOS;
using Foundation;
using KyoshinEewViewer.Core;
using KyoshinEewViewer.Services.TelegramPublishers.Dmdata;
using ReactiveUI.Avalonia;
using Splat;

namespace KyoshinEewViewer.iOS;

[Register("AppDelegate")]
public partial class AppDelegate : AvaloniaAppDelegate<App>
{
	public AppDelegate()
		// IActivatableLifetime は AvaloniaLocator へ後から束縛されるため取得タイミングに依存する。
		// AppDelegate 自身のイベントなら確実に購読できる
		=> ((IAvaloniaAppDelegate)this).Activated += (_, e) =>
		{
			LogHost.Default.Info($"アクティベーションを受信しました: {e.GetType().Name} / Kind={e.Kind}");
			if (e is not ProtocolActivatedEventArgs protocolArgs)
				return;
			LogHost.Default.Info($"URL アクティベーション: {protocolArgs.Uri.Scheme}://{protocolArgs.Uri.Host}");
			if (Locator.Current.GetService<IDmdataAuthenticator>() is DmdataCustomSchemeAuthenticator authenticator)
				authenticator.HandleCallback(protocolArgs.Uri);
			else
				LogHost.Default.Warn("認可の実装が登録されていないためコールバックを処理できません");
		};

	protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
		=> base.CustomizeAppBuilder(builder)
			.UseKeviFonts()
			.UseReactiveUI(_ => { });
}
