using Avalonia.Controls;
using Avalonia.Input;
using ReactiveUI;
using System;

namespace KyoshinEewViewer.Series.Earthquake;

public partial class EarthquakeView : UserControl
{
	public EarthquakeView()
	{
		InitializeComponent();

		HistoryScrollLoader.Attach(HistoryList);

		// 別ウィンドウやオーバーレイの内側に置かれることがあるため、ウィンドウ幅ではなく自身の幅で判定する
		this.WhenAnyValue(v => v.Bounds).Subscribe(_ => UpdateViewWidth());
		DataContextChanged += (s, e) => UpdateViewWidth();
	}

	private void UpdateViewWidth()
	{
		if (DataContext is EarthquakeSeries series)
			series.ViewWidth = Bounds.Width;
	}

	private void SheetBackdrop_PointerPressed(object? sender, PointerPressedEventArgs e)
		=> (DataContext as EarthquakeSeries)?.CloseSheets();
}
