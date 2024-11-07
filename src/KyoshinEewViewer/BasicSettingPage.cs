using Avalonia.Controls;
using KyoshinEewViewer.Series;
using ReactiveUI;

namespace KyoshinEewViewer;
public class BasicSettingPage<T>(string? icon, string title, ISettingPage[] subPages) : ReactiveObject, ISettingPage where T : Control, new()
{
	private bool _isVisible = true;
	public bool IsVisible
	{
		get => _isVisible;
		set => this.RaiseAndSetIfChanged(ref _isVisible, value);
	}

	public string? Icon => icon;
	public string Title => title;
	public Control DisplayControl => new T();

	public ISettingPage[] SubPages => subPages;
}
