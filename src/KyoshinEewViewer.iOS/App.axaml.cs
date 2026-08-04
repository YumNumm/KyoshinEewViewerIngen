using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using KyoshinEewViewer.Core;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Core.Models.Events;
using KyoshinEewViewer.CustomControl;
using KyoshinEewViewer.Notification;
using KyoshinEewViewer.Series;
using KyoshinEewViewer.Services;
using KyoshinEewViewer.Services.TelegramPublishers.Dmdata;
using KyoshinEewViewer.ViewModels;
using KyoshinEewViewer.Views;
using ReactiveUI;
using Splat;
using System;
using System.Linq;
using System.Reactive.Linq;

namespace KyoshinEewViewer.iOS;

public class App : Application
{
	private static Control? _mainView;
	public static Control? MainView
	{
		get => _mainView;
		set {
			_mainView = value;
			KyoshinEewViewerApp.TopLevelControl = TopLevel.GetTopLevel(value);
			if (value is null || KyoshinEewViewerApp.TopLevelControl is not null)
				return;

			// ISingleViewApplicationLifetime.MainView への代入より先にこのセッターが走るため、
			// この時点では TopLevel に未接続で GetTopLevel が null を返す。
			// TopLevelControl は Launcher (URL やブラウザを開く) の起点なので、接続後に取り直す
			void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
			{
				value.AttachedToVisualTree -= OnAttached;
				KyoshinEewViewerApp.TopLevelControl = TopLevel.GetTopLevel(value);
			}
			value.AttachedToVisualTree += OnAttached;
		}
	}

	private readonly OverlaySubWindowsService _subWindowsService = new();
	private readonly DmdataCustomSchemeAuthenticator _dmdataAuthenticator = new();

	public override void Initialize() => AvaloniaXamlLoader.Load(this);

	public override void OnFrameworkInitializationCompleted()
	{
		KyoshinEewViewerApp.Application = this;

		// フォントリソースのURLメモ
		// "avares://KyoshinEewViewer.Core/Assets/Fonts/NotoSansJP-Regular.otf"
		// "avares://KyoshinEewViewer.Core/Assets/Fonts/NotoSansJP-Bold.otf"
		// "avares://KyoshinEewViewer.Core/Assets/Fonts/FontAwesome6Free-Solid-900.otf"
		// "avares://FluentAvalonia/Fonts/FluentAvalonia.ttf"
		// DMDATA の認可後にカスタム URL スキームで戻ってくる
		if (ApplicationLifetime is IActivatableLifetime activatable)
			activatable.Activated += (_, e) =>
			{
				if (e is ProtocolActivatedEventArgs protocolArgs)
					_dmdataAuthenticator.HandleCallback(protocolArgs.Uri);
			};

		if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
		{
			// iOS では標準出力やログファイルを確認できないため、致命的な例外は画面上に表示する
			Dispatcher.UIThread.UnhandledException += (_, e) =>
			{
				e.Handled = true;
				ShowFatalError(singleViewPlatform, e.Exception);
			};

			try
			{
				SetupMainView(singleViewPlatform);
			}
			catch (Exception ex)
			{
				ShowFatalError(singleViewPlatform, ex);
			}
		}

		base.OnFrameworkInitializationCompleted();
	}

	private static void ShowFatalError(ISingleViewApplicationLifetime singleViewPlatform, Exception ex)
	{
		LogHost.Default.Error(ex, "致命的な例外が発生しました");
		try
		{
			singleViewPlatform.MainView = MainView = FatalErrorView.Create(ex);
		}
		catch (Exception inner)
		{
			LogHost.Default.Error(inner, "例外内容の表示に失敗しました");
		}
	}

	private void SetupMainView(ISingleViewApplicationLifetime singleViewPlatform)
	{
		KyoshinEewViewerApp.Selector = ThemeSelector.Create(null);
		KyoshinEewViewerApp.Selector.EnableThemes(this);

		var config = Locator.Current.RequireService<KyoshinEewViewerConfiguration>();

		// サブウィンドウをオーバーレイとして重ねるため、MainView を Panel で包む
		var host = new Panel();
		var mainView = new MainView
		{
			DataContext = Locator.Current.RequireService<MainViewModel>(),
		};
		host.Children.Add(mainView);
		_subWindowsService.OverlayHost = host;
		singleViewPlatform.MainView = MainView = host;

		ApplyHostBackground(host);

		MessageBus.Current.Listen<ShowSettingWindowRequested>()
			.Subscribe(_ => Dispatcher.UIThread.Post(_subWindowsService.ShowSettingWindow));

		// NOTE: 旧バージョンからの移行
		if (config.Theme.WindowThemeName is string windowTheme)
		{
			config.Theme.WindowTheme = KyoshinEewViewerApp.Selector.WindowThemes?.FirstOrDefault(t => t.Meta.Identifier == windowTheme)?.Meta ?? config.Theme.WindowTheme;
			config.Theme.WindowThemeName = null; // 古い設定を削除
		}
		if (config.Theme.IntensityThemeName is string intensityTheme)
		{
			config.Theme.IntensityTheme = KyoshinEewViewerApp.Selector.IntensityThemes?.FirstOrDefault(t => t.Meta.Identifier == intensityTheme)?.Meta ?? config.Theme.IntensityTheme;
			config.Theme.IntensityThemeName = null; // 古い設定を削除
		}

		KyoshinEewViewerApp.Selector.ApplyTheme(config.Theme.WindowTheme, config.Theme.IntensityTheme);
		KyoshinEewViewerApp.Selector.WhenAnyValue(x => x.SelectedIntensityTheme)
			.Subscribe(x =>
			{
				if (x == null) return;
				config.Theme.IntensityTheme = x.Meta;
				// MainView は論理ツリー未接続の場合にテーマリソースを解決できないため Application から引く
				Dispatcher.UIThread.Post(() => FixedObjectRenderer.UpdateIntensityPaintCache(this));
			});
		KyoshinEewViewerApp.Selector.WhenAnyValue(x => x.SelectedWindowTheme)
			.Subscribe(_ => Dispatcher.UIThread.Post(() => ApplyHostBackground(host)));

		if (config.ShowWizard)
			Dispatcher.UIThread.Post(async () =>
			{
				await _subWindowsService.ShowDialogSetupWizardWindow(_ => { });
				config.ShowWizard = false;
				ConfigurationLoader.Save(config);
			});
	}

	// セーフエリア分の余白は MainView の外側に出るため、テーマの背景色で埋める
	private void ApplyHostBackground(Panel host)
	{
		if (this.FindResource("MainBackgroundColor") is Color color)
			host.Background = new SolidColorBrush(color);
	}

	/// <summary>
	/// override RegisterServices register custom service
	/// </summary>
	public override void RegisterServices()
	{
		Locator.CurrentMutable.RegisterConstant(Locator.Current, typeof(IReadonlyDependencyResolver));
		Locator.CurrentMutable.RegisterLazySingleton(ConfigurationLoader.Load, typeof(KyoshinEewViewerConfiguration));
		Locator.CurrentMutable.RegisterLazySingleton(() => new SeriesController(), typeof(SeriesController));
		Locator.CurrentMutable.RegisterConstant(_subWindowsService, typeof(ISubWindowsService));
		Locator.CurrentMutable.RegisterConstant(_dmdataAuthenticator, typeof(IDmdataAuthenticator));
		// NotificationService から解決された時点で通知の許可を要求する
		Locator.CurrentMutable.RegisterLazySingleton(() => (NotificationProvider)new Notification.IosNotificationProvider(), typeof(NotificationProvider));
		var config = Locator.Current.RequireService<KyoshinEewViewerConfiguration>();
		LoggingAdapter.Setup(config);

		// 共通の既定クライアントはループバック URI しか登録されていないため iOS では使えない。
		// 既定値のままの場合のみ差し替え、ユーザーが独自に設定したクライアントは尊重する
		if (config.Dmdata.OAuthClientId == KyoshinEewViewerConfiguration.DmdataConfig.DefaultOAuthClientId)
			config.Dmdata.OAuthClientId = DmdataCustomSchemeAuthenticator.ClientId;

		SetupIOC(Locator.GetLocator());
		base.RegisterServices();
	}

	public static void SetupIOC(IDependencyResolver resolver)
	{
		KyoshinEewViewerApp.SetupIOC(resolver);
		SplatRegistrations.SetupIOC(resolver);
	}
}
