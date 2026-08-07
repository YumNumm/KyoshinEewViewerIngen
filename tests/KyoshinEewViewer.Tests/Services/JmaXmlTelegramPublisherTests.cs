using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Services;
using KyoshinEewViewer.Services.TelegramPublishers.JmaXml;
using Splat;

namespace KyoshinEewViewer.Tests.Services;

public class JmaXmlTelegramPublisherTests
{
	private static JmaXmlTelegramPublisher CreatePublisher(KyoshinEewViewerConfiguration config)
	{
		var logManager = Locator.Current.GetService<ILogManager>() ?? new DefaultLogManager();
		return new JmaXmlTelegramPublisher(logManager, new TimerService(logManager, config), new InformationCacheService(logManager), config);
	}

	[Fact(DisplayName = "無効化されている場合はフィードへ問い合わせずサポートカテゴリが空になる")]
	public async Task 無効化時はサポートカテゴリが空になる()
	{
		// 有効なままだと気象庁のフィードへ HEAD リクエストを送ってしまうため、
		// 構築前から無効にしておく
		var config = new KyoshinEewViewerConfiguration();
		config.JmaXml.Enable = false;

		var publisher = CreatePublisher(config);

		// 通信を伴わずに即座に空が返る
		Assert.Empty(await publisher.GetSupportedCategoriesAsync());
	}

	[Fact(DisplayName = "無効化されている場合は購読を開始しても取得対象にならない")]
	public async Task 無効化時は購読を開始しても取得対象にならない()
	{
		var config = new KyoshinEewViewerConfiguration();
		config.JmaXml.Enable = false;

		var publisher = CreatePublisher(config);
		publisher.Start([InformationCategory.Earthquake, InformationCategory.Tsunami]);

		// Start が握り潰されるためサポートカテゴリは空のまま
		Assert.Empty(await publisher.GetSupportedCategoriesAsync());
	}

	[Fact(DisplayName = "既定では有効になっている")]
	public void 既定では有効になっている()
		=> Assert.True(new KyoshinEewViewerConfiguration().JmaXml.Enable);
}
