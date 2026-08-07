using System.Net;
using System.Text;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Tests.Services;

/// <summary>
/// 生成された API クライアントのリクエスト組み立てと応答の解釈を検証する
/// </summary>
public class EqMonitorApiClientTests
{
	/// <summary>
	/// 応答を差し替えつつ、実際に組み立てられた URI を記録するハンドラ
	/// </summary>
	private sealed class StubHandler(string responseBody) : HttpMessageHandler
	{
		public Uri? RequestUri { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			RequestUri = request.RequestUri;
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
			});
		}
	}

	private static (Generated.EqMonitorApiClient Client, StubHandler Handler) CreateClient(string responseBody)
	{
		var handler = new StubHandler(responseBody);
		// 接続先はコードに持たせず HttpClient.BaseAddress からのみ与える
		var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:12345/") };
		return (new Generated.EqMonitorApiClient(httpClient), handler);
	}

	[Fact(DisplayName = "地震情報一覧の取得で絞り込み条件がクエリへ反映される")]
	public async Task 地震情報一覧の取得()
	{
		var (client, handler) = CreateClient("""
		{"next_token":"dGVzdA==","items":[{"event_id":"20251212191438","status":"NORMAL",
		"earthquake_type":"NORMAL","origin_time":"2025-12-12T19:14:38+09:00",
		"origin_time_precision":"SECOND","hypocenter":{"code":"288","name":"宮城県沖",
		"coordinates":{"latitude":38.1,"longitude":142.3},
		"magnitude":{"type":"NORMAL","value":7.1},"depth":{"type":"NORMAL","value":60}},
		"intensity":{"max_intensity":"6-"},"datasources":["JMA_DISASTER_INFORMATION_XML"],
		"telegram_types":["VXSE53"]}]}
		""");

		var response = await client.GetV2EarthquakeAsync(
			limit: "50",
			intensityGte: Generated.JmaIntensity._5Minus,
			statuses: [Generated.TelegramStatus.NORMAL],
			sortOrder: Generated.SortOrder.DESC);

		var uri = handler.RequestUri;
		Assert.NotNull(uri);
		Assert.Equal("/v2/earthquake", uri.AbsolutePath);
		Assert.Contains("limit=50", uri.Query);
		// 震度は JSON 上の表記でクエリに載る必要がある
		Assert.Contains("intensityGte=5-", Uri.UnescapeDataString(uri.Query));
		Assert.Contains("statuses=NORMAL", uri.Query);
		Assert.Contains("sortOrder=DESC", uri.Query);

		var item = Assert.Single(response.Items);
		Assert.Equal("20251212191438", item.Event_id);
		Assert.Equal("宮城県沖", item.Hypocenter!.Name);
		Assert.Equal(7.1, item.Hypocenter.Magnitude.Value);
		Assert.Equal(Generated.JmaIntensity._6Minus, item.Intensity!.Max_intensity);
		Assert.Equal("dGVzdA==", response.Next_token);
	}

	[Fact(DisplayName = "最新の緊急地震速報を取得できる")]
	public async Task 最新の緊急地震速報の取得()
	{
		var (client, handler) = CreateClient("""
		{"items":[{"event_id":"20251212191438","type":"VXSE45","status":"NORMAL",
		"info_type":"PUBLICATION","serial_no":3,"headline":"強い揺れに警戒してください",
		"is_canceled":false,"is_warning":true,"is_last_info":false,
		"origin_time":"2025-12-12T19:14:38+09:00","arrival_time":null,
		"hypocenter":{"code":"288","name":"宮城県沖",
		"coordinates":{"latitude":38.1,"longitude":142.3},"magnitude":7.1,"depth":60},
		"forecast_intensity":{"max_intensity":{"value":"5+","is_over":false},"regions":[]},
		"accuracy":{"epicenter":4,"hypocenter":9,"depth":5,
		"magnitude_calculation":6,"number_of_magnitude_calculation":10},
		"is_plum":false,"editorial_office":"仙台管区気象台",
		"report_time":"2025-12-12T19:14:50+09:00"}]}
		""");

		var response = await client.GetV2EewLatestAsync();

		Assert.Equal("/v2/eew/latest", handler.RequestUri!.AbsolutePath);

		var item = Assert.Single(response.Items);
		Assert.Equal(3, item.Serial_no);
		Assert.True(item.Is_warning);
		Assert.Equal("強い揺れに警戒してください", item.Headline);
		Assert.Equal(Generated.JmaIntensity._5Plus, item.Forecast_intensity!.Max_intensity!.Value);
		Assert.Equal(9, item.Accuracy!.Hypocenter);
	}
}
