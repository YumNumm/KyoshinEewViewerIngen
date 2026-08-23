using FluentAvalonia.UI.Controls;

namespace KyoshinEewViewer.ViewModels;

/// <summary>
/// <see cref="FANavigationView"/> のペインを、画面幅に応じて常時表示とオーバーレイ表示で切り替えるビューモデルの基底
/// </summary>
public abstract class NavigationPaneViewModelBase : ViewModelBase
{
	/// <summary>
	/// ペインを常時表示するために必要な最小幅
	/// </summary>
	protected abstract double PaneVisibleMinWidth { get; }

	/// <summary>
	/// 画面幅に応じた切り替えを行うかどうか
	/// false にすると <see cref="NavigationViewPaneDisplayMode"/> を継承先から固定できる
	/// </summary>
	protected bool IsPaneLayoutAdaptive { get; set; } = true;

	private double _viewWidth = double.PositiveInfinity;
	/// <summary>
	/// ビューの表示幅。ウィンドウ拡大率を適用したあとの論理サイズ
	/// </summary>
	public double ViewWidth
	{
		get => _viewWidth;
		set {
			if (_viewWidth == value)
				return;
			SetProperty(ref _viewWidth, value);
			if (!IsPaneLayoutAdaptive)
				return;
			IsNarrowLayout = value < PaneVisibleMinWidth;
		}
	}

	private bool _isNarrowLayout;
	/// <summary>
	/// ペインをオーバーレイ表示に切り替える画面幅かどうか
	/// </summary>
	public bool IsNarrowLayout
	{
		get => _isNarrowLayout;
		private set {
			if (_isNarrowLayout == value)
				return;
			SetProperty(ref _isNarrowLayout, value);
			// LeftMinimal ではペインが画面外に隠れ、開いたときのみオーバーレイ表示になる
			NavigationViewPaneDisplayMode = value
				? FANavigationViewPaneDisplayMode.LeftMinimal
				: FANavigationViewPaneDisplayMode.Left;
			// FANavigationView 側もモード変更に応じてペインを開閉するが、
			// その結果はバインディングの初期化順によってこちらの値で上書きされうるため、開閉状態も明示する
			IsNavigationPaneOpen = !value;
		}
	}

	private FANavigationViewPaneDisplayMode _navigationViewPaneDisplayMode = FANavigationViewPaneDisplayMode.Left;
	public FANavigationViewPaneDisplayMode NavigationViewPaneDisplayMode
	{
		get => _navigationViewPaneDisplayMode;
		set => SetProperty(ref _navigationViewPaneDisplayMode, value);
	}

	private bool _isNavigationPaneOpen = true;
	public bool IsNavigationPaneOpen
	{
		get => _isNavigationPaneOpen;
		set => SetProperty(ref _isNavigationPaneOpen, value);
	}

	public void ToggleNavigationPane()
		=> IsNavigationPaneOpen = !IsNavigationPaneOpen;
}
