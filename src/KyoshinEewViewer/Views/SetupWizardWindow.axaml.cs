using Avalonia.Controls;
using System;

namespace KyoshinEewViewer.Views;
public partial class SetupWizardWindow : Window
{
	public event Action? Continued;

	public SetupWizardWindow()
	{
		InitializeComponent();
		View.Continued += () => Continued?.Invoke();
	}
}
