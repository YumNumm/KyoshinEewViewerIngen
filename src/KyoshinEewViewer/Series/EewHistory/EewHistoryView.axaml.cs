using Avalonia.Controls;
using Avalonia.Input;
using ReactiveUI;
using System;

namespace KyoshinEewViewer.Series.EewHistory;

public partial class EewHistoryView : UserControl
{
	public EewHistoryView()
	{
		InitializeComponent();

		HistoryScrollLoader.Attach(EventHistoryList);
		HistoryScrollLoader.Attach(EventHistorySheetList);

		// 別ウィンドウやオーバーレイの内側に置かれることがあるため、ウィンドウ幅ではなく自身の幅で判定する
		this.WhenAnyValue(v => v.Bounds).Subscribe(_ => UpdateViewWidth());
		DataContextChanged += (_, _) => UpdateViewWidth();
	}

	private void UpdateViewWidth()
	{
		if (DataContext is EewHistorySeries series)
			series.ViewWidth = Bounds.Width;
	}

	private void SheetBackdrop_PointerPressed(object? sender, PointerPressedEventArgs e)
		=> (DataContext as EewHistorySeries)?.CloseSheets();
}
