using Android.App;
using Android.Content;
using Android.Content.PM;
using Avalonia.Android;
using Avalonia.Controls.ApplicationLifetimes;
using KyoshinEewViewer.Core;
using KyoshinEewViewer.Services.TelegramPublishers.Dmdata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KyoshinEewViewer.Android;

[Activity(
	Theme = "@style/MyTheme.NoActionBar",
	Icon = "@drawable/icon",
	MainLauncher = true,
	Exported = true,
	// 外部ブラウザからのコールバックを新しいインスタンスではなく起動中のインスタンスへ届ける
	LaunchMode = LaunchMode.SingleTask,
	ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
// DMDATA の認可コールバックの受け口。iOS の Info.plist CFBundleURLTypes に相当する。
// スキームとホストは DmdataCustomSchemeAuthenticator.RedirectUri と一致させること
[IntentFilter(
	[Intent.ActionView],
	Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
	DataScheme = "net.yumnumm.kyoshineewviewer",
	DataHost = "login-callback")]
public class MainActivity : AvaloniaMainActivity
{
	public MainActivity()
		// IActivatableLifetime は AvaloniaLocator へ後から束縛されるため取得タイミングに依存する。
		// Activity 自身のイベントなら確実に購読できる
		=> ((IAvaloniaActivity)this).Activated += (_, e) =>
		{
			if (e is not ProtocolActivatedEventArgs protocolArgs)
				return;
			AppLog.Default.LogInformation("URL アクティベーション: {Scheme}://{Host}", protocolArgs.Uri.Scheme, protocolArgs.Uri.Host);
			if (ServiceLocator.Current.GetService<IDmdataAuthenticator>() is DmdataCustomSchemeAuthenticator authenticator)
				authenticator.HandleCallback(protocolArgs.Uri);
			else
				AppLog.Default.LogWarning("認可の実装が登録されていないためコールバックを処理できません");
		};
}
