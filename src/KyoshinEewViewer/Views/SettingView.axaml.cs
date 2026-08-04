using Avalonia.Controls;
using KyoshinEewViewer.ViewModels;
using ReactiveUI;
using System;

namespace KyoshinEewViewer.Views;

public partial class SettingView : UserControl
{
	public SettingView()
	{
		InitializeComponent();

		// iOS ではオーバーレイの内側に置かれるため、ウィンドウ幅ではなく自身の幅で判定する
		this.WhenAnyValue(v => v.Bounds).Subscribe(_ => UpdateViewWidth());
		DataContextChanged += (s, e) => UpdateViewWidth();
	}

	private void UpdateViewWidth()
	{
		if (DataContext is SettingWindowViewModel vm)
			vm.ViewWidth = Bounds.Width;
	}
}
