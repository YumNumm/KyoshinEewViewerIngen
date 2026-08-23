using System.Net;
using System.Text;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Tests.Services;

/// <summary>
/// EQMonitor の端末登録とリアルタイム接続用 API 契約を検証する
/// </summary>
public class EqMonitorRealtimeApiClientTests
{
	private sealed class StubHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
	{
		private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new(responses);

		public List<HttpRequestMessage> Requests { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			var clone = new HttpRequestMessage(request.Method, request.RequestUri);
			if (request.Content != null)
				clone.Content = new StringContent(
					await request.Content.ReadAsStringAsync(cancellationToken),
					Encoding.UTF8,
					"application/json");
			Requests.Add(clone);

			var response = _responses.Dequeue();
			return new HttpResponseMessage(response.Status)
			{
				Content = new StringContent(response.Body, Encoding.UTF8, "application/json"),
			};
		}
	}

	[Fact(DisplayName = "端末をDesktop・日本語として登録し、リアルタイム接続チケットを取得できる")]
	public async Task 端末登録とリアルタイム接続チケットの取得()
	{
		var handler = new StubHandler(
			(HttpStatusCode.Created, """{"deviceId":"device-id","deviceToken":"device-token","expiresAt":null}"""),
			(HttpStatusCode.OK, """{"url":"wss://socket.invalid/realtime","expiresAt":"2026-08-23T01:00:00Z","issuedAt":"2026-08-23T00:00:00Z"}"""));
		using var httpClient = new HttpClient(handler)
		{
			BaseAddress = new Uri("https://api.invalid/"),
		};
		var client = new Generated.EqMonitorApiClient(httpClient);

		var device = await client.PostV2DeviceAsync(new Generated.DeviceRegisterBody
		{
			Type = Generated.DeviceType.DESKTOP,
			Locale = Generated.DeviceLocale.Ja,
		});
		var ticket = await client.GetV2RealtimeTicketAsync();

		Assert.Equal("device-id", device.DeviceId);
		Assert.Equal("device-token", device.DeviceToken);
		Assert.Equal(new Uri("wss://socket.invalid/realtime"), ticket.Url);

		Assert.Collection(handler.Requests,
			request =>
			{
				Assert.Equal(HttpMethod.Post, request.Method);
				Assert.Equal("/v2/device", request.RequestUri!.AbsolutePath);
				Assert.Equal("{\"type\":\"DESKTOP\",\"locale\":\"ja\"}",
					request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
			},
			request =>
			{
				Assert.Equal(HttpMethod.Get, request.Method);
				Assert.Equal("/v2/realtime/ticket", request.RequestUri!.AbsolutePath);
			});
	}
}
