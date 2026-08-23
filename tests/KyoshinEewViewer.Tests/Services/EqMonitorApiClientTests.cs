using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Services.EqMonitor;
using Moq;
using Splat;
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
		public Dictionary<string, string[]> RequestHeaders { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			RequestUri = request.RequestUri;
			foreach (var header in request.Headers)
				RequestHeaders[header.Key] = [.. header.Value];
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

	[Fact(DisplayName = "接続先は設定値を優先し、未設定ならビルド時の既定値へフォールバックする")]
	public void 接続先の解決()
	{
		// 実在しないホスト名を使い、テストから外部へ出ないようにする
		const string Configured = "https://configuredexample";
		const string BuiltIn = "https://builtinexample";

		// 設定値がある場合は既定値より優先する
		Assert.Equal(new Uri("https://configuredexample/"), EqMonitorApiProvider.ResolveBaseUri(Configured, BuiltIn));

		// 設定値が空白のみの場合も未設定として扱い、既定値へ落ちる
		foreach (var configured in new string?[] { null, "", "   " })
			Assert.Equal(new Uri("https://builtinexample/"), EqMonitorApiProvider.ResolveBaseUri(configured, BuiltIn));

		// どちらも無ければ利用できない
		Assert.Null(EqMonitorApiProvider.ResolveBaseUri(null, null));
		Assert.Null(EqMonitorApiProvider.ResolveBaseUri("", "  "));

		// BaseAddress で最後のパス要素が失われないよう末尾にスラッシュを補う
		Assert.Equal(new Uri("https://configuredexample/v2/"), EqMonitorApiProvider.ResolveBaseUri($"{Configured}/v2", null));
		Assert.Equal(new Uri("https://builtinexample/v2/"), EqMonitorApiProvider.ResolveBaseUri(null, $"{BuiltIn}/v2"));

		// http/https 以外や URL として解釈できない値は受け付けない
		Assert.Null(EqMonitorApiProvider.ResolveBaseUri("ftp://configuredexample/", null));
		Assert.Null(EqMonitorApiProvider.ResolveBaseUri("not-a-url", null));
		Assert.Null(EqMonitorApiProvider.ResolveBaseUri(null, "not-a-url"));
	}

	[Fact(DisplayName = "User-Agent とビルド番号が EQMonitor の求める形式になっている")]
	public void 識別ヘッダの形式()
	{
		// KyoshinEewViewer-{プラットフォーム}-v{x.x.x}
		Assert.Matches(new Regex(@"^KyoshinEewViewer-(Android|iOS|macOS|Windows|Browser|Linux|Unknown)-v\d+\.\d+\.\d+$"),
			EqMonitorApiProvider.UserAgent);
		Assert.Matches(new Regex(@"^\d+$"), EqMonitorApiProvider.BuildNumber);
	}

	[Fact(DisplayName = "HTTPリクエストへUser-Agent・ビルド番号・同じ接続先の端末IDを設定する")]
	public async Task HTTP識別ヘッダの設定()
	{
		var handler = new StubHandler(
			"""{"url":"wss://socket.invalid/realtime","expiresAt":"2026-08-23T01:00:00Z","issuedAt":"2026-08-23T00:00:00Z"}""");
		var config = new KyoshinEewViewerConfiguration();
		config.EqMonitor.BaseUrl = "https://api.invalid";
		config.EqMonitor.DeviceId = "persisted-device";
		config.EqMonitor.DeviceRegisteredBaseUrl = "https://api.invalid/";
		var logManager = new Mock<ILogManager>();
		logManager.Setup(x => x.GetLogger(It.IsAny<Type>()))
			.Returns(new Mock<IFullLogger>().Object);
		using var provider = new EqMonitorApiProvider(logManager.Object, config)
		{
			CreateHttpClient = () => new HttpClient(handler, disposeHandler: false),
		};

		await provider.GetClient()!.GetV2RealtimeTicketAsync();

		Assert.Equal([EqMonitorApiProvider.UserAgent], handler.RequestHeaders["User-Agent"]);
		Assert.Equal([EqMonitorApiProvider.BuildNumber], handler.RequestHeaders["x-eqmonitor-build"]);
		Assert.Equal(["persisted-device"], handler.RequestHeaders["x-eqmonitor-device-id"]);
	}
}
