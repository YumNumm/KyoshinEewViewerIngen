using Avalonia.Controls;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Series;
using Splat;

namespace KyoshinEewViewer.Services.EqMonitor;

public class EqMonitorSettingPage : ISettingPage
{
	public bool IsVisible => true;

	public string? Icon => null;

	public string Title => "EQMonitor(試験中)";

	public Control DisplayControl => new EqMonitorPage() { DataContext = this };

	public ISettingPage[] SubPages => [];

	public KyoshinEewViewerConfiguration Config { get; }

	/// <summary>
	/// ベース URL 入力欄のプレースホルダ<br/>
	/// 既定の接続先が埋め込まれている場合でも、接続先そのものは表示しない
	/// </summary>
	public string BaseUrlPlaceholder =>
		EqMonitorApiProvider.BuiltInBaseUrl is null ? "https://example.com" : "既定の接続先を使用します";

	public EqMonitorSettingPage(KyoshinEewViewerConfiguration config)
	{
		SplatRegistrations.RegisterLazySingleton<EqMonitorSettingPage>();

		Config = config;
	}
}
