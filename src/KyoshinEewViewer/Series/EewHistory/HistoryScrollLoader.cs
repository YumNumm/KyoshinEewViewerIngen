using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace KyoshinEewViewer.Series.EewHistory;

/// <summary>
/// EEWイベント履歴の一覧が末尾までスクロールされた際に、さらに過去の情報を読み込ませる
/// </summary>
internal static class HistoryScrollLoader
{
	/// <summary>
	/// 画面を埋めるための連続読み込みの上限
	/// </summary>
	private const int MaxRefillCount = 5;

	/// <summary>
	/// 一覧へスクロール監視を取り付ける
	/// </summary>
	public static void Attach(Control list)
		=> list.AddHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged);

	private static void OnScrollChanged(object? sender, RoutedEventArgs e)
	{
		if (sender is not Control { DataContext: EewHistorySeries series })
			return;
		if (e.Source is not ScrollViewer scroll)
			return;
		if (!ShouldLoadMore(scroll))
			return;

		_ = LoadUntilFilledAsync(scroll, series);
	}

	/// <summary>
	/// 追加の読み込みを行うべき状態か
	/// </summary>
	private static bool ShouldLoadMore(ScrollViewer scroll)
	{
		var scrollableHeight = scroll.Extent.Height - scroll.Viewport.Height;

		// 一覧が画面に収まっている間はスクロールできないため、画面が埋まるまで読み込む
		if (scrollableHeight <= 0)
			return scroll.Viewport.Height > 0;

		// 利用者が下へスクロールして初めて読み込む
		if (scroll.Offset.Y <= 0)
			return false;

		var threshold = Math.Min(scroll.Viewport.Height, 200);
		return scrollableHeight - scroll.Offset.Y <= threshold;
	}

	/// <summary>
	/// 一覧が画面を埋めるまで読み込む
	/// </summary>
	private static async Task LoadUntilFilledAsync(ScrollViewer scroll, EewHistorySeries series)
	{
		for (var i = 0; i < MaxRefillCount; i++)
		{
			await series.LoadMoreHistoryAsync();
			if (!series.CanLoadMoreHistory)
				return;

			await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
			if (!ShouldLoadMore(scroll))
				return;
		}
	}
}
