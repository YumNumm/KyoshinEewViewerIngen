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
using KyoshinEewViewer.Series;
using KyoshinEewViewer.Services;
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
		}
	}

	private readonly OverlaySubWindowsService _subWindowsService = new();

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

		// MainView は全画面描画を要求するため、ステータスバーやホームインジケータに
		// 重ならないようセーフエリアの分をこちらで空ける
		host.AttachedToVisualTree += (_, _) =>
		{
			if (TopLevel.GetTopLevel(host)?.InsetsManager is not { } insetsManager)
				return;
			mainView.Margin = insetsManager.SafeAreaPadding;
			insetsManager.SafeAreaChanged += (_, e) => mainView.Margin = e.SafeAreaPadding;
		};
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
		var config = Locator.Current.RequireService<KyoshinEewViewerConfiguration>();
		LoggingAdapter.Setup(config);

		SetupIOC(Locator.GetLocator());
		base.RegisterServices();
	}

	public static void SetupIOC(IDependencyResolver resolver)
	{
		KyoshinEewViewerApp.SetupIOC(resolver);
		SplatRegistrations.SetupIOC(resolver);
	}
}
