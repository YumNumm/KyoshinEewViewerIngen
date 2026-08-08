using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace KyoshinEewViewer.Series.Earthquake;

public partial class EarthquakeSearchView : UserControl
{
	/// <summary>
	/// 「地図で範囲を選択」が押された
	/// </summary>
	public event Action? MapSelectionRequested;

	public EarthquakeSearchView()
	{
		InitializeComponent();
		SelectOnMapButton.Click += OnSelectOnMapClick;
	}

	private void OnSelectOnMapClick(object? sender, RoutedEventArgs e)
		=> MapSelectionRequested?.Invoke();
}
