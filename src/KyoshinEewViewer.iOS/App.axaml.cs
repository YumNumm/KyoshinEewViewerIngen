using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using KyoshinEewViewer.Core;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Core.Models.Events;
using KyoshinEewViewer.CustomControl;
using KyoshinEewViewer.Notification;
using KyoshinEewViewer.Services;
using KyoshinEewViewer.Services.Audio;
using KyoshinEewViewer.Services.TelegramPublishers.Dmdata;
using KyoshinEewViewer.ViewModels;
using KyoshinEewViewer.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using R3;
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
		AppLog.Default.LogError(ex, "致命的な例外が発生しました");
		try
		{
			singleViewPlatform.MainView = MainView = FatalErrorView.Create(ex);
		}
		catch (Exception inner)
		{
			AppLog.Default.LogError(inner, "例外内容の表示に失敗しました");
		}
	}

	private void SetupMainView(ISingleViewApplicationLifetime singleViewPlatform)
	{
		KyoshinEewViewerApp.Selector = ThemeSelector.Create(null);
		KyoshinEewViewerApp.Selector.EnableThemes(this);

		var config = ServiceLocator.Current.RequireService<KyoshinEewViewerConfiguration>();

		// サブウィンドウをオーバーレイとして重ねるため、MainView を Panel で包む
		var host = new Panel();
		var mainView = new MainView
		{
			DataContext = ServiceLocator.Current.RequireService<MainViewModel>(),
		};
		host.Children.Add(mainView);
		_subWindowsService.OverlayHost = host;
		singleViewPlatform.MainView = MainView = host;

		ApplyHostBackground(host);

		StrongReferenceMessenger.Default.Register<ShowSettingWindowRequested>(this,
			(_, _) => Dispatcher.UIThread.Post(_subWindowsService.ShowSettingWindow));

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
		KyoshinEewViewerApp.Selector.ObservePropertyChanged(x => x.SelectedIntensityTheme)
			.Subscribe(x =>
			{
				if (x == null) return;
				config.Theme.IntensityTheme = x.Meta;
				// MainView は論理ツリー未接続の場合にテーマリソースを解決できないため Application から引く
				Dispatcher.UIThread.Post(() => FixedObjectRenderer.UpdateIntensityPaintCache(this));
			});
		KyoshinEewViewerApp.Selector.ObservePropertyChanged(x => x.SelectedWindowTheme)
			.Subscribe(x =>
			{
				if (x == null) return;
				config.Theme.WindowTheme = x.Meta;
				Dispatcher.UIThread.Post(() => ApplyHostBackground(host));
			});

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
		var services = new ServiceCollection();
		services.SetupConfigurationAndLogging(config =>
		{
			// 共通の既定クライアントはループバック URI しか登録されていないため iOS では使えない。
			// 既定値のままの場合のみ差し替え、ユーザーが独自に設定したクライアントは尊重する
			if (config.Dmdata.OAuthClientId == KyoshinEewViewerConfiguration.DmdataConfig.DefaultOAuthClientId)
				config.Dmdata.OAuthClientId = DmdataCustomSchemeAuthenticator.ClientId;
		});
		services.AddKyoshinEewViewer();
		services.AddSingleton<ISubWindowsService>(_subWindowsService);
		services.AddSingleton<IDmdataAuthenticator>(_dmdataAuthenticator);
		// NotificationService から解決された時点で通知の許可を要求する
		services.AddSingleton<NotificationProvider, Notification.IosNotificationProvider>();
		// BASS のネイティブが存在しないため AVFoundation の実装へ差し替える
		services.AddSingleton<IAudioBackend, Audio.IosAudioBackend>();

		ServiceLocator.SetProvider(services.BuildServiceProvider());
		base.RegisterServices();
	}
}
