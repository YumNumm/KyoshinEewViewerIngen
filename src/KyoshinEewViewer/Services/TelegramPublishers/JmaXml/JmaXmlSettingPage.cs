using Avalonia.Controls;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Series;
using Splat;

namespace KyoshinEewViewer.Services.TelegramPublishers.JmaXml;

public class JmaXmlSettingPage : ISettingPage
{
	public bool IsVisible => true;

	public string? Icon => null;

	public string Title => "気象庁防災情報XML";

	public Control DisplayControl => new JmaXmlPage() { DataContext = this };

	public ISettingPage[] SubPages => [];

	public KyoshinEewViewerConfiguration Config { get; }

	public JmaXmlSettingPage(KyoshinEewViewerConfiguration config)
	{
		SplatRegistrations.RegisterLazySingleton<JmaXmlSettingPage>();

		Config = config;
	}
}
