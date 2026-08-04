using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;

namespace KyoshinEewViewer.iOS;

/// <summary>
/// 例外内容を画面上に表示するためのビュー。
/// テーマ初期化前に落ちても表示できるよう、DynamicResource を使わず色を直接指定している。
/// </summary>
public static class FatalErrorView
{
	public static Control Create(Exception ex)
	{
		var panel = new StackPanel
		{
			Margin = new Thickness(24),
			Spacing = 12,
		};
		panel.Children.Add(new TextBlock
		{
			Text = "起動に失敗しました",
			FontSize = 24,
			FontWeight = FontWeight.Bold,
			Foreground = new SolidColorBrush(Color.FromRgb(255, 96, 96)),
		});
		panel.Children.Add(new TextBlock
		{
			Text = "以下の内容を開発者に報告してください。",
			FontSize = 14,
			Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
		});
		panel.Children.Add(new SelectableTextBlock
		{
			Text = ex.ToString(),
			FontFamily = FontFamily.Parse("monospace"),
			FontSize = 11,
			Foreground = new SolidColorBrush(Colors.White),
			TextWrapping = TextWrapping.Wrap,
		});

		return new Border
		{
			Background = new SolidColorBrush(Color.FromRgb(24, 24, 28)),
			Child = new ScrollViewer
			{
				HorizontalAlignment = HorizontalAlignment.Stretch,
				VerticalAlignment = VerticalAlignment.Stretch,
				Content = panel,
			},
		};
	}
}
