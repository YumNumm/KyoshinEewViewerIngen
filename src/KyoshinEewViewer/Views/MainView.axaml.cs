using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using KyoshinEewViewer.Core;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Core.Models.Events;
using KyoshinEewViewer.Map;
using KyoshinEewViewer.Services;
using KyoshinEewViewer.ViewModels;
using Microsoft.Extensions.Logging;
using R3;
using SkiaSharp;
using System;
using System.IO;
using System.Linq;

namespace KyoshinEewViewer.Views;
public partial class MainView : UserControl
{
	/// <summary>
	/// システム UI に隠れない領域の余白。デスクトップでは常に 0 になる
	/// </summary>
	public static readonly StyledProperty<Thickness> SafeAreaPaddingProperty
		= AvaloniaProperty.Register<MainView, Thickness>(nameof(SafeAreaPadding));

	public Thickness SafeAreaPadding
	{
		get => GetValue(SafeAreaPaddingProperty);
		set => SetValue(SafeAreaPaddingProperty, value);
	}

	/// <summary>
	/// ナビゲーションペインの項目 1 つ分の幅
	/// </summary>
	private const double NavigationPaneItemWidth = 64;

	/// <summary>
	/// ナビゲーションペインの項目に適用する余白。
	/// ペインは常に画面左端にあるため、右側のセーフエリアは項目の配置に影響しない
	/// </summary>
	public static readonly StyledProperty<Thickness> NavigationPanePaddingProperty
		= AvaloniaProperty.Register<MainView, Thickness>(nameof(NavigationPanePadding));

	public Thickness NavigationPanePadding
	{
		get => GetValue(NavigationPanePaddingProperty);
		set => SetValue(NavigationPanePaddingProperty, value);
	}

	/// <summary>
	/// ナビゲーションペインの幅。項目の幅を確保したまま左側のセーフエリアの分だけ広げる
	/// </summary>
	public static readonly StyledProperty<double> NavigationPaneLengthProperty
		= AvaloniaProperty.Register<MainView, double>(nameof(NavigationPaneLength), NavigationPaneItemWidth);

	public double NavigationPaneLength
	{
		get => GetValue(NavigationPaneLengthProperty);
		set => SetValue(NavigationPaneLengthProperty, value);
	}

	/// <summary>
	/// 音量調整フライアウトを閉じるまでの猶予。ボタンとフライアウトの間をポインタが通過する時間を考慮している
	/// </summary>
	private static readonly TimeSpan VolumeFlyoutCloseDelay = TimeSpan.FromMilliseconds(350);

	private DispatcherTimer VolumeFlyoutCloseTimer { get; }
	/// <summary>ミュートボタンを最後に操作したポインタの種別。キーボード操作などポインタを伴わない場合は null</summary>
	private PointerType? LastMuteButtonPointerType { get; set; }
	/// <summary>音量調整フライアウト内でポインタが押下されている (スライダーをドラッグ中の可能性がある)</summary>
	private bool IsVolumeFlyoutPointerPressed { get; set; }

	public MainView()
	{
		InitializeComponent();

		VolumeFlyoutCloseTimer = new DispatcherTimer { Interval = VolumeFlyoutCloseDelay };
		VolumeFlyoutCloseTimer.Tick += (s, e) => CloseVolumeFlyoutIfPointerAway();

		// Click イベントではポインタの種別が取得できないため、直前の押下時に記録しておく
		MuteButton.AddHandler(PointerPressedEvent, (s, e) => LastMuteButtonPointerType = e.Pointer.Type, RoutingStrategies.Tunnel);
		if (FlyoutBase.GetAttachedFlyout(MuteButton) is Flyout volumeFlyout)
		{
			// ライトディスミス用のオーバーレイにミュートボタンへの操作が吸われないようにする
			volumeFlyout.OverlayInputPassThroughElement = MuteButton;
			if (volumeFlyout.Content is Control volumeFlyoutContent)
			{
				// フライアウトは別のポップアップに載るため、ボタン側のイベントだけでは閉じる判定ができない
				volumeFlyoutContent.PointerEntered += (s, e) => VolumeFlyoutCloseTimer.Stop();
				volumeFlyoutContent.PointerExited += (s, e) =>
				{
					if (e.Pointer.Type != PointerType.Touch)
						RestartVolumeFlyoutCloseTimer();
				};
				// スライダーのドラッグ中はポインタがフライアウトから外れるため、押下状態を見て閉じないようにする
				volumeFlyoutContent.AddHandler(PointerPressedEvent, (s, e) => IsVolumeFlyoutPointerPressed = true, RoutingStrategies.Tunnel);
				volumeFlyoutContent.AddHandler(PointerReleasedEvent, (s, e) => IsVolumeFlyoutPointerPressed = false, RoutingStrategies.Tunnel, true);
			}
		}
		// フライアウトの外でポインタが離された場合にも押下状態を解除する
		AddHandler(PointerReleasedEvent, (s, e) => IsVolumeFlyoutPointerPressed = false, RoutingStrategies.Tunnel, true);

		KyoshinEewViewerApp.Selector?.ObservePropertyChanged(x => x.SelectedWindowTheme).Where(x => x != null)
				.Subscribe(x => {
					Map.RefreshResourceCache(x!.Theme);
					MiniMap.RefreshResourceCache(x!.Theme);
				});

		var config = ServiceLocator.Current.RequireService<KyoshinEewViewerConfiguration>();
		config.Map.ObservePropertyChanged(x => x.DisableManualMapControl).Subscribe(x =>
		{
			HomeButton.IsVisible = !x;
			Map.IsDisableManualControl = x;
		});
		HomeButton.IsVisible = !config.Map.DisableManualMapControl;
		Map.IsDisableManualControl = config.Map.DisableManualMapControl;

		Map.Zoom = 6;
		Map.CenterLocation = new KyoshinMonitorLib.Location(36.474f, 135.264f);

		// R3 の ThrottleLast は dotnet/reactive の Sample 相当
		Observable.CombineLatest(
				Map.ObservePropertyChanged(m => m.CenterLocation).AsUnitObservable(),
				Map.ObservePropertyChanged(m => m.Zoom).AsUnitObservable())
			.ThrottleLast(TimeSpan.FromSeconds(.1)).Subscribe(m =>
		{
			var config = ServiceLocator.Current.RequireService<KyoshinEewViewerConfiguration>();
			Dispatcher.UIThread.Post(new Action(() =>
			{
				MiniMapContainer.IsVisible = config.Map.UseMiniMap && Map.IsNavigatedPosition(new RectD(config.Map.Location1.CastPoint(), config.Map.Location2.CastPoint()));
				ResetMinimapPosition();
			}));
		});

		MiniMap.ObservePropertyChanged(m => m.Bounds).Subscribe(b => ResetMinimapPosition());
		AttachedToVisualTree += (s, e) =>
		{
			ResetMinimapPosition();
		};

		// ScaledRoot は LayoutTransformControl の子であるため、ウィンドウ拡大率を適用したあとの論理幅が得られる
		ScaledRoot.ObservePropertyChanged(p => p.Bounds).Subscribe(_ => UpdateViewWidth());
		DataContextChanged += (s, e) => UpdateViewWidth();

		StrongReferenceMessenger.Default.Register<MapNavigationRequest>(this, (_, x) =>
		{
			if (!config.Map.AutoFocus)
				return;
			NavigateMap(x, config);
		});

		StrongReferenceMessenger.Default.Register<RegistMapPositionRequested>(this, (_, x) =>
		{
			var halfPaddedRect = new PointD(Map.PaddedRect.Width / 2, -Map.PaddedRect.Height / 2);
			var centerPixel = Map.CenterLocation.ToPixel(Map.Zoom);

			config.Map.Location1 = (centerPixel + halfPaddedRect).ToLocation(Map.Zoom);
			config.Map.Location2 = (centerPixel - halfPaddedRect).ToLocation(Map.Zoom);
		});

		StrongReferenceMessenger.Default.Register<MapImageSaveRequested>(this, (_, x) =>
		{
			if (x.TargetPath is { } path)
				SaveMapToFile(path);
		});

		// タッチだけでなくマウスの長押しでも拾えるようにする
		OpenSettingWindowButton.SetValue(InputElement.IsHoldingEnabledProperty, true);
		OpenSettingWindowButton.SetValue(InputElement.IsHoldWithMouseEnabledProperty, true);
		OpenSettingWindowButton.AddHandler(InputElement.HoldingEvent, OnSettingButtonHolding);

		AttachedToVisualTree += (s, e) =>
		{
			if (TopLevel.GetTopLevel(this) is { } topLevel && topLevel.InsetsManager is { } insetsManager)
			{
				// ステータスバーやホームバーは隠さず、その裏まで描画する (edge-to-edge)
				insetsManager.IsSystemBarVisible = true;
				insetsManager.DisplayEdgeToEdgePreference = true;
				// 地図とサイドバーの背景は画面全体に描画し、操作対象のみ SafeAreaPadding で内側に寄せる。
				// SafeAreaPadding は物理ピクセルを RenderScaling で割った値だが、Android では
				// RenderScaling が 1 のまま SafeAreaChanged が先に飛んでくる。その値をそのまま使うと
				// 物理ピクセル相当の過大な余白になるため、スケール確定時にも取り直す
				void UpdateSafeAreaPadding()
				{
					var padding = insetsManager.SafeAreaPadding;
					SafeAreaPadding = padding;
					// 横向きではカットアウトによって左右どちらかに大きな余白が入る。
					// ペインの幅は項目 1 つ分しかないため、余白をそのまま引くと項目が収まらず切れてしまう
					NavigationPanePadding = new Thickness(padding.Left, padding.Top, 0, padding.Bottom);
					NavigationPaneLength = NavigationPaneItemWidth + padding.Left;
				}
				UpdateSafeAreaPadding();
				insetsManager.SafeAreaChanged += (_, _) => UpdateSafeAreaPadding();
				topLevel.ScalingChanged += (_, _) => UpdateSafeAreaPadding();
			}
		};
	}

	private void UpdateViewWidth()
	{
		if (DataContext is MainViewModel vm)
			vm.ViewWidth = ScaledRoot.Bounds.Width;
	}

	private void ResetMinimapPosition()
	{
		if (!MiniMap.IsVisible)
			return;
		//MiniMap.Navigate(new RectD(new PointD(24.127, 123.585), new PointD(28.546, 129.803)), TimeSpan.Zero, true);
		MiniMap.Navigate(new RectD(new PointD(22.289, 121.207), new PointD(31.128, 132.100)), TimeSpan.Zero, true);
	}

	// リリースビルドではデバッグメニューを隠しているため、長押しで解放する
	private void OnSettingButtonHolding(object? sender, HoldingRoutedEventArgs e)
	{
		if (e.HoldingState != HoldingState.Started)
			return;
		e.Handled = true;
		AppLog.Default.LogInformation("設定ボタンの長押しによりデバッグメニューを表示します");
		ServiceLocator.Current.RequireService<SettingWindowViewModel>().ShowDebugMenu();
		StrongReferenceMessenger.Default.Send(new ShowSettingWindowRequested());
	}

	private void MuteButton_Click(object? sender, RoutedEventArgs e)
	{
		var flyout = FlyoutBase.GetAttachedFlyout(MuteButton) as Flyout;
		// タッチ操作ではホバーができないため、フライアウトが閉じている間のタップはミュートせず音量調整を開くだけにする
		if (LastMuteButtonPointerType == PointerType.Touch && flyout?.IsOpen != true)
			FlyoutBase.ShowAttachedFlyout(MuteButton);
		else
			(DataContext as MainViewModel)?.ToggleMute();
		// キーボード操作の Click に直前のタッチ操作の種別が引き継がれないようにする
		LastMuteButtonPointerType = null;
	}

	private void MuteButton_PointerEntered(object? sender, PointerEventArgs e)
	{
		// タッチ操作はタップで開くため、ホバー扱いにはしない
		if (e.Pointer.Type == PointerType.Touch)
			return;
		VolumeFlyoutCloseTimer.Stop();
		FlyoutBase.ShowAttachedFlyout(MuteButton);
	}

	private void MuteButton_PointerExited(object? sender, PointerEventArgs e)
	{
		if (e.Pointer.Type == PointerType.Touch)
			return;
		RestartVolumeFlyoutCloseTimer();
	}

	private void RestartVolumeFlyoutCloseTimer()
	{
		VolumeFlyoutCloseTimer.Stop();
		VolumeFlyoutCloseTimer.Start();
	}

	private void CloseVolumeFlyoutIfPointerAway()
	{
		VolumeFlyoutCloseTimer.Stop();
		if (FlyoutBase.GetAttachedFlyout(MuteButton) is not Flyout flyout)
			return;
		// スライダーのドラッグ中やポインタが乗っている間は閉じない
		if (IsVolumeFlyoutPointerPressed || MuteButton.IsPointerOver || (flyout.Content is Control content && content.IsPointerOver))
		{
			VolumeFlyoutCloseTimer.Start();
			return;
		}
		flyout.Hide();
	}

	private void HomeButton_Click(object? sender, RoutedEventArgs e)
	{
		var config = ServiceLocator.Current.RequireService<KyoshinEewViewerConfiguration>();
		// 自動ナビゲーションが無効な場合はシリーズの範囲ではなくホームポジションに戻す
		if (!config.Map.AutoFocus)
		{
			NavigateToHome(config);
			return;
		}
		var request = (DataContext as MainViewModel)?.SelectedSeries?.MapNavigationRequest ?? new MapNavigationRequest(null);
		NavigateMap(request, config);
	}

	private void NavigateMap(MapNavigationRequest? request, KyoshinEewViewerConfiguration config)
	{
		if (request?.Bound is { } rect)
		{
			if (request.MustBound is { } mustBound)
				Map.Navigate(rect, config.Map.AutoFocusAnimation ? TimeSpan.FromSeconds(.3) : TimeSpan.Zero, mustBound);
			else
				Map.Navigate(rect, config.Map.AutoFocusAnimation ? TimeSpan.FromSeconds(.3) : TimeSpan.Zero);
		}
		else
			NavigateToHome(config);
	}

	private void NavigateToHome(KyoshinEewViewerConfiguration config)
	{
		Map?.Navigate(
			new RectD(config.Map.Location1.CastPoint(), config.Map.Location2.CastPoint()),
			config.Map.AutoFocusAnimation ? TimeSpan.FromSeconds(.3) : TimeSpan.Zero);
	}

	private void SaveMapToFile(string filePath)
	{
		try
		{
			var ext = Path.GetExtension(filePath).ToLowerInvariant();
			using var stream = File.Create(filePath);

			switch (ext)
			{
				case ".skp":
					Map.SaveAsPictureToStream(stream);
					break;
				case ".png":
					Map.SaveToStream(stream, SKEncodedImageFormat.Png);
					break;
				case ".jpg":
				case ".jpeg":
					Map.SaveToStream(stream, SKEncodedImageFormat.Jpeg);
					break;
				case ".webp":
					Map.SaveToStream(stream, SKEncodedImageFormat.Webp);
					break;
				default:
					Map.SaveAsPictureToStream(stream);
					break;
			}
		}
		catch
		{
			// 保存失敗時は何もしない
		}
	}
}
