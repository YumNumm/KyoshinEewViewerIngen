using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using KyoshinEewViewer.Core;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Core.Models.Events;
using KyoshinEewViewer.CustomControl;
using KyoshinEewViewer.Series;
using KyoshinEewViewer.Services;
using KyoshinEewViewer.Services.Audio;
using KyoshinEewViewer.Services.TelegramPublishers.Dmdata;
using KyoshinEewViewer.ViewModels;
using KyoshinEewViewer.Views;
using R3;
using System;
using System.Linq;
using System.Reactive.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace KyoshinEewViewer.Android;

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
			KyoshinEewViewerApp.Selector = ThemeSelector.Create(null);
			KyoshinEewViewerApp.Selector.EnableThemes(this);

			var config = ServiceLocator.Current.RequireService<KyoshinEewViewerConfiguration>();

			// サブウィンドウをオーバーレイとして重ねるため、MainView を Panel で包む
			var host = new Panel();
			host.Children.Add(new MainView
			{
				DataContext = ServiceLocator.Current.RequireService<MainViewModel>(),
			});
			_subWindowsService.OverlayHost = host;
			singleViewPlatform.MainView = MainView = host;

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
				});
		}

		base.OnFrameworkInitializationCompleted();
	}

	/// <summary>
	/// override RegisterServices register custom service
	/// </summary>
	public override void RegisterServices()
	{
		var services = new ServiceCollection();
		services.SetupConfigurationAndLogging(config =>
		{
			// 共通の既定クライアントはループバック URI しか登録されていないため Android では使えない。
			// 既定値のままの場合のみ差し替え、ユーザーが独自に設定したクライアントは尊重する
			if (config.Dmdata.OAuthClientId == KyoshinEewViewerConfiguration.DmdataConfig.DefaultOAuthClientId)
				config.Dmdata.OAuthClientId = DmdataCustomSchemeAuthenticator.ClientId;
		});
		services.AddKyoshinEewViewer();
		services.AddSingleton<ISubWindowsService>(_subWindowsService);
		services.AddSingleton<IDmdataAuthenticator>(_dmdataAuthenticator);
		// BASS のネイティブが存在しないため MediaPlayer の実装へ差し替える
		services.AddSingleton<IAudioBackend, Audio.AndroidAudioBackend>();

		ServiceLocator.SetProvider(services.BuildServiceProvider());
		base.RegisterServices();
	}

	public void OpenSettingsClicked(object sender, EventArgs args)
		=> StrongReferenceMessenger.Default.Send(new ShowSettingWindowRequested());

}
