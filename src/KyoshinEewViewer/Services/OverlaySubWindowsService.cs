using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using KyoshinEewViewer.Core;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Series;
using KyoshinEewViewer.ViewModels;
using KyoshinEewViewer.Views;
using Splat;
using System;
using System.Threading.Tasks;

namespace KyoshinEewViewer.Services;

/// <summary>
/// Window を生成できないプラットフォーム (iOS / Android / ブラウザ) 向けの <see cref="ISubWindowsService"/>。
/// サブウィンドウの内容を <see cref="OverlayHost"/> に重ねて表示する。
/// </summary>
public class OverlaySubWindowsService : ISubWindowsService
{
	/// <summary>
	/// オーバーレイを載せる Panel。MainView を子に持つ Panel を割り当てる
	/// </summary>
	public Panel? OverlayHost { get; set; }

	// Window を生成できないため、ダイアログの親として使えるものが無い
	public SettingWindow? SettingWindow => null;
	public SetupWizardWindow? SetupWizardWindow => null;
	public DebugWindow? DebugWindow => null;

	public bool IsShuttingDown { get; set; }

	public event Action<SeriesBase>? SeriesWindowOpened { add { } remove { } }
	public event Action<SeriesBase>? SeriesWindowClosed { add { } remove { } }

	private Control? _overlay;

	public void ShowSettingWindow()
	{
		if (_overlay != null)
			return;

		Show("設定", new SettingView
		{
			DataContext = Locator.Current.RequireService<SettingWindowViewModel>(),
		}, closable: true, onClosed: () => ConfigurationLoader.Save(Locator.Current.RequireService<KyoshinEewViewerConfiguration>()));
	}

	public Task ShowDialogSetupWizardWindow(Action<SetupWizardWindow> opened)
	{
		// opened は Desktop で MainWindow を差し替えるためのもので、オーバーレイでは使わない
		var completion = new TaskCompletionSource();

		var view = new SetupWizardView
		{
			DataContext = Locator.Current.RequireService<SetupWizardWindowViewModel>(),
		};
		view.Continued += () =>
		{
			Close();
			completion.TrySetResult();
		};

		if (!Show("セットアップウィザード", view, closable: false, onClosed: () => completion.TrySetResult()))
			completion.TrySetResult();

		return completion.Task;
	}

	private bool Show(string title, Control content, bool closable, Action? onClosed)
	{
		if (OverlayHost is not { } host)
			return false;

		var header = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("*,auto"),
			Background = KyoshinEewViewerApp.Application?.FindResource("DockTitleBackgroundColor") is Color c
				? new SolidColorBrush(c)
				: null,
		};
		header.Children.Add(new TextBlock
		{
			Text = title,
			FontSize = 20,
			Margin = new Thickness(12, 8),
			VerticalAlignment = VerticalAlignment.Center,
		});
		if (closable)
		{
			var closeButton = new Button
			{
				Content = "閉じる",
				Margin = new Thickness(8),
				MinWidth = 100,
			};
			closeButton.Click += (_, _) => Close();
			header.Children.Add(closeButton);
			Grid.SetColumn(closeButton, 1);
		}

		var body = new Grid { RowDefinitions = new RowDefinitions("auto,*") };
		body.Children.Add(header);
		body.Children.Add(content);
		Grid.SetRow(content, 1);

		// ステータスバーやホームインジケータに重ならないよう、セーフエリアの分だけ内側に寄せる
		var safeArea = TopLevel.GetTopLevel(host)?.InsetsManager?.SafeAreaPadding ?? default;

		_overlay = new Border
		{
			// 背後の MainView への操作を遮る
			Background = new SolidColorBrush(Colors.Black, 0.5),
			Child = new Border
			{
				Margin = new Thickness(safeArea.Left + 16, safeArea.Top + 16, safeArea.Right + 16, safeArea.Bottom + 16),
				CornerRadius = new CornerRadius(8),
				ClipToBounds = true,
				Child = body,
			},
		};
		_onClosed = onClosed;
		host.Children.Add(_overlay);
		return true;
	}

	private Action? _onClosed;

	private void Close()
	{
		if (_overlay is null)
			return;

		OverlayHost?.Children.Remove(_overlay);
		_overlay = null;

		var onClosed = _onClosed;
		_onClosed = null;
		onClosed?.Invoke();
	}

	public void ShowDebugWindow() { }
	public Task ShowDialogWindowThemeEditWindow(ThemeSelector.WindowTheme? theme) => Task.CompletedTask;
	public Task ShowDialogIntensityThemeEditWindow(ThemeSelector.IntensityTheme? theme) => Task.CompletedTask;
	public void ShowSeriesWindow(SeriesBase series) { }
	public void CloseSeriesWindow(SeriesBase series) { }
	public bool IsSeriesSeparated(SeriesBase series) => false;
	public void CloseAllSeriesWindows() { }
}
