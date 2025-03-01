using Avalonia.Controls;

namespace KyoshinEewViewer.Series;

public interface ISettingPage
{
	public bool IsVisible { get; }
	public string? Icon { get; }
	public string Title { get; }
	public Control DisplayControl { get; }

	public ISettingPage[] SubPages { get; }
}
