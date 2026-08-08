using System.Net;
using System.Text;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Tests.Services;

/// <summary>
/// EEW履歴向け API クライアントのリクエスト組み立てを検証する
/// </summary>
public class EqMonitorEewHistoryApiClientTests
{
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
		var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:12345/") };
		return (new Generated.EqMonitorApiClient(httpClient), handler);
	}

	[Fact(DisplayName = "EEW履歴一覧の取得で絞り込み条件がクエリへ反映される")]
	public async Task EEW履歴一覧の取得()
	{
		var (client, handler) = CreateClient("""
		{"next_token":"dGVzdA==","items":[{"event_id":"20251212191438","type":"VXSE45","status":"NORMAL",
		"info_type":"PUBLICATION","serial_no":3,"headline":"強い揺れに警戒してください",
		"is_canceled":false,"is_warning":true,"is_last_info":true,
		"origin_time":"2025-12-12T19:14:38+09:00","arrival_time":null,
		"hypocenter":{"code":"288","name":"宮城県沖",
		"coordinates":{"latitude":38.1,"longitude":142.3},"magnitude":7.1,"depth":60},
		"forecast_intensity":{"max_intensity":{"value":"5+","is_over":false},"regions":[]},
		"accuracy":{"epicenter":4,"hypocenter":9,"depth":5,
		"magnitude_calculation":6,"number_of_magnitude_calculation":10},
		"is_plum":false,"editorial_office":"仙台管区気象台",
		"report_time":"2025-12-12T19:14:50+09:00"}]}
		""");

		var response = await client.GetV2EewAsync(
			limit: "50",
			cursor: "dGVzdA==",
			magnitudeGte: "5.0",
			magnitudeLte: "8.0",
			depthGte: "10",
			depthLte: "100",
			intensityGte: Generated.JmaIntensity._5Minus,
			intensityLte: Generated.JmaIntensity._6Plus,
			originTimeGte: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.FromHours(9)),
			originTimeLte: new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.FromHours(9)),
			statuses: [Generated.TelegramStatus.NORMAL, Generated.TelegramStatus.TRAINING],
			isWarning: "true");

		var uri = handler.RequestUri;
		Assert.NotNull(uri);
		Assert.Equal("/v2/eew", uri.AbsolutePath);
		var query = Uri.UnescapeDataString(uri.Query);
		Assert.Contains("limit=50", query);
		Assert.Contains("cursor=dGVzdA==", query);
		Assert.Contains("magnitudeGte=5.0", query);
		Assert.Contains("magnitudeLte=8.0", query);
		Assert.Contains("depthGte=10", query);
		Assert.Contains("depthLte=100", query);
		Assert.Contains("intensityGte=5-", query);
		Assert.Contains("intensityLte=6+", query);
		Assert.Contains("originTimeGte=2025-01-01", query);
		Assert.Contains("originTimeLte=2025-12-31", query);
		Assert.Contains("statuses=NORMAL", query);
		Assert.Contains("statuses=TRAINING", query);
		Assert.Contains("isWarning=true", query);

		var item = Assert.Single(response.Items);
		Assert.Equal("20251212191438", item.Event_id);
		Assert.Equal(3, item.Serial_no);
		Assert.True(item.Is_last_info);
		Assert.Equal("dGVzdA==", response.Next_token);
	}

	[Fact(DisplayName = "イベントID指定で全報を取得できる")]
	public async Task EEW全報の取得()
	{
		var (client, handler) = CreateClient("""
		{"items":[
		{"event_id":"20251212191438","type":"VXSE45","status":"NORMAL",
		"info_type":"PUBLICATION","serial_no":1,"headline":null,
		"is_canceled":false,"is_warning":false,"is_last_info":false,
		"origin_time":"2025-12-12T19:14:38+09:00","arrival_time":null,
		"hypocenter":null,"forecast_intensity":null,"accuracy":null,
		"is_plum":false,"editorial_office":null,
		"report_time":"2025-12-12T19:14:40+09:00"},
		{"event_id":"20251212191438","type":"VXSE45","status":"NORMAL",
		"info_type":"PUBLICATION","serial_no":2,"headline":null,
		"is_canceled":false,"is_warning":true,"is_last_info":true,
		"origin_time":"2025-12-12T19:14:38+09:00","arrival_time":null,
		"hypocenter":null,"forecast_intensity":null,"accuracy":null,
		"is_plum":false,"editorial_office":null,
		"report_time":"2025-12-12T19:14:50+09:00"}
		]}
		""");

		var response = await client.GetV2EewByEventIdAsync("20251212191438");

		Assert.Equal("/v2/eew/20251212191438", handler.RequestUri!.AbsolutePath);
		Assert.Equal(2, response.Items.Count);
		Assert.Equal(1, response.Items[0].Serial_no);
		Assert.Equal(2, response.Items[1].Serial_no);
	}
}
