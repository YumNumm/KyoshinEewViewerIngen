using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.iOS;
using Foundation;
using KyoshinEewViewer.Core;
using KyoshinEewViewer.Services.TelegramPublishers.Dmdata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KyoshinEewViewer.iOS;

[Register("AppDelegate")]
public partial class AppDelegate : AvaloniaAppDelegate<App>
{
	public AppDelegate()
		// IActivatableLifetime は AvaloniaLocator へ後から束縛されるため取得タイミングに依存する。
		// AppDelegate 自身のイベントなら確実に購読できる
		=> ((IAvaloniaAppDelegate)this).Activated += (_, e) =>
		{
			AppLog.Default.LogInformation("アクティベーションを受信しました: {EventType} / Kind={Kind}", e.GetType().Name, e.Kind);
			if (e is not ProtocolActivatedEventArgs protocolArgs)
				return;
			AppLog.Default.LogInformation("URL アクティベーション: {Scheme}://{Host}", protocolArgs.Uri.Scheme, protocolArgs.Uri.Host);
			if (ServiceLocator.Current.GetService<IDmdataAuthenticator>() is DmdataCustomSchemeAuthenticator authenticator)
				authenticator.HandleCallback(protocolArgs.Uri);
			else
				AppLog.Default.LogWarning("認可の実装が登録されていないためコールバックを処理できません");
		};

	protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
		=> base.CustomizeAppBuilder(builder)
			.UseKeviFonts();
}
