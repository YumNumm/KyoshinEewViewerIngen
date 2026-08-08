using System.Net;
using System.Text;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Services.NetworkDebug;

namespace KyoshinEewViewer.Tests.Services;

/// <summary>
/// 通信内容の記録が、記録対象の取捨選択と伏せ字を正しく行い、
/// なおかつ呼び出し元の通信を壊さないことを検証する
/// </summary>
public class NetworkCaptureHandlerTests
{
	/// <summary>
	/// 任意のレスポンスを返すハンドラ
	/// </summary>
	private sealed class StubHandler(HttpContent content, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromResult(new HttpResponseMessage(statusCode) { Content = content });
	}

	/// <summary>
	/// 必ず例外を投げるハンドラ
	/// </summary>
	private sealed class ThrowingHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> throw new HttpRequestException("接続できませんでした");
	}

	private static (HttpClient Client, NetworkDebugRecorder Recorder) CreateClient(HttpMessageHandler inner, bool enabled = true)
	{
		var config = new KyoshinEewViewerConfiguration();
		config.Debug.RecordNetworkTraffic = enabled;
		var recorder = new NetworkDebugRecorder(config);
		return (new HttpClient(new NetworkCaptureHandler(inner, recorder)), recorder);
	}

	private static HttpTransaction SingleHttp(NetworkDebugRecorder recorder)
		=> Assert.IsType<HttpTransaction>(Assert.Single(recorder.GetAll()));

	[Fact(DisplayName = "記録が無効な場合は通信内容を保持しない")]
	public async Task 記録が無効な場合()
	{
		var (client, recorder) = CreateClient(
			new StubHandler(new StringContent("""{"value":1}""", Encoding.UTF8, "application/json")),
			enabled: false);

		var response = await client.GetAsync("http://localhost:12345/test");

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Empty(recorder.GetAll());
	}

	[Fact(DisplayName = "リクエストとレスポンスが記録され、認証情報が伏せ字になる")]
	public async Task 記録内容と伏せ字()
	{
		// DM-D.S.S のトークン応答を模したレスポンス
		var (client, recorder) = CreateClient(new StubHandler(new StringContent(
			"""{"access_token":"secret-access","refresh_token":"secret-refresh","expires_in":3600}""",
			Encoding.UTF8,
			"application/json")));

		using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:12345/oauth2/v1/token")
		{
			Content = new StringContent(
				"grant_type=authorization_code&code=secret-code&client_id=visible-client",
				Encoding.UTF8,
				"application/x-www-form-urlencoded"),
		};
		request.Headers.TryAddWithoutValidation("Authorization", "Bearer secret-token");
		request.Headers.TryAddWithoutValidation("User-Agent", "KEViTest");

		await client.SendAsync(request);

		var transaction = SingleHttp(recorder);
		Assert.Equal("POST", transaction.Method);
		Assert.Equal("/oauth2/v1/token", transaction.Uri.AbsolutePath);
		Assert.Equal(200, transaction.StatusCode);

		// Authorization は伏せ字、それ以外のヘッダはそのまま残る
		Assert.Equal("***", Assert.Single(transaction.RequestHeaders, h => h.Key == "Authorization").Value);
		Assert.Equal("KEViTest", Assert.Single(transaction.RequestHeaders, h => h.Key == "User-Agent").Value);

		// フォームの認可コードは伏せ字、それ以外の項目は残る
		Assert.NotNull(transaction.RequestBody);
		Assert.DoesNotContain("secret-code", transaction.RequestBody);
		Assert.Contains("code=***", transaction.RequestBody);
		Assert.Contains("client_id=visible-client", transaction.RequestBody);

		// JSON のトークンは伏せ字、それ以外の項目は残る
		Assert.NotNull(transaction.ResponseBody);
		Assert.DoesNotContain("secret-access", transaction.ResponseBody);
		Assert.DoesNotContain("secret-refresh", transaction.ResponseBody);
		Assert.Contains("3600", transaction.ResponseBody);
	}

	[Theory(DisplayName = "テキストとして扱えない Content-Type のボディは記録しない")]
	[InlineData("image/png")]
	[InlineData("application/octet-stream")]
	[InlineData("application/zip")]
	public async Task 記録対象外の形式(string mediaType)
	{
		var content = new ByteArrayContent([1, 2, 3, 4]);
		content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
		var (client, recorder) = CreateClient(new StubHandler(content));

		await client.GetAsync("http://localhost:12345/image");

		var transaction = SingleHttp(recorder);
		// 行としては残るが、ボディは保持しない
		Assert.Null(transaction.ResponseBody);
		Assert.Equal(mediaType, transaction.ContentType);
	}

	[Fact(DisplayName = "記録後も呼び出し元がレスポンスボディを読める")]
	public async Task 記録後もボディを読める()
	{
		const string body = """{"items":[{"code":"288","name":"宮城県沖"}]}""";
		var (client, recorder) = CreateClient(new StubHandler(new StringContent(body, Encoding.UTF8, "application/json")));

		var response = await client.GetAsync("http://localhost:12345/v2/earthquake");

		// 記録のためにバッファへ読み込んでも、呼び出し元は通常どおり読める
		Assert.Equal(body, await response.Content.ReadAsStringAsync());
		await using var stream = await response.Content.ReadAsStreamAsync();
		Assert.Equal(body, await new StreamReader(stream).ReadToEndAsync());

		Assert.Equal(body, SingleHttp(recorder).ResponseBody);
	}

	[Fact(DisplayName = "上限を超えるボディは切り詰めて記録する")]
	public async Task 上限を超えるボディ()
	{
		var body = new string('a', NetworkDebugRecorder.MaxBodyLength + 1000);
		var (client, recorder) = CreateClient(new StubHandler(new StringContent(body, Encoding.UTF8, "text/plain")));

		var response = await client.GetAsync("http://localhost:12345/large");

		var recorded = SingleHttp(recorder).ResponseBody;
		Assert.NotNull(recorded);
		Assert.StartsWith(new string('a', NetworkDebugRecorder.MaxBodyLength), recorded);
		Assert.Contains("1000 文字を省略", recorded);

		// 切り詰めるのは記録内容だけで、呼び出し元へは元のまま渡す
		Assert.Equal(body, await response.Content.ReadAsStringAsync());
	}

	[Fact(DisplayName = "通信に失敗した場合も記録し、例外はそのまま呼び出し元へ伝える")]
	public async Task 通信失敗時()
	{
		var (client, recorder) = CreateClient(new ThrowingHandler());

		await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("http://localhost:12345/fail"));

		var transaction = SingleHttp(recorder);
		Assert.Equal("接続できませんでした", transaction.Error);
		Assert.Null(transaction.StatusCode);
	}

	[Fact(DisplayName = "保持件数の上限を超えると古い記録から破棄する")]
	public void 保持件数の上限()
	{
		var config = new KyoshinEewViewerConfiguration();
		config.Debug.RecordNetworkTraffic = true;
		var recorder = new NetworkDebugRecorder(config);

		for (var i = 0; i < NetworkDebugRecorder.MaxEntryCount + 10; i++)
			recorder.Record(new WebSocketTransaction
			{
				Endpoint = "wss://localhost:12345/socket",
				Direction = WebSocketDirection.Receive,
				Payload = i.ToString(),
			});

		var all = recorder.GetAll();
		Assert.Equal(NetworkDebugRecorder.MaxEntryCount, all.Count);
		Assert.Equal("10", Assert.IsType<WebSocketTransaction>(all[0]).Payload);
	}

	[Theory(DisplayName = "WebSocket の接続先からクエリの認証情報を取り除く")]
	// DM-D.S.S の WebSocket URL は ticket をクエリに載せる
	[InlineData("wss://localhost:12345/v2/socket?ticket=secret", "wss://localhost:12345/v2/socket")]
	[InlineData("wss://localhost:12345/socket", "wss://localhost:12345/socket")]
	[InlineData("", "")]
	public void 接続先のクエリ除去(string endpoint, string expected)
		=> Assert.Equal(expected, NetworkCaptureMasker.SanitizeEndpoint(endpoint));
}
