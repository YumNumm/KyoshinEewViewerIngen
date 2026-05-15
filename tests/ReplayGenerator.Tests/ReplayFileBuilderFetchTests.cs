using Microsoft.Extensions.Logging.Abstractions;
using ReplayGenerator.Domain;
using System.Net;
using Xunit;

namespace ReplayGenerator.Tests;

public class ReplayFileBuilderFetchTests
{
	private sealed class StubHandler : HttpMessageHandler
	{
		private readonly Queue<HttpResponseMessage> _responses;
		public int CallCount { get; private set; }

		public StubHandler(IEnumerable<HttpResponseMessage> responses)
		{
			_responses = new Queue<HttpResponseMessage>(responses);
		}

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			CallCount++;
			if (_responses.Count == 0)
				throw new InvalidOperationException("テスト用レスポンスが不足しています");
			return Task.FromResult(_responses.Dequeue());
		}
	}

	private static KyoshinImageRetryOptions FastOptions(int maxAttempts) => new()
	{
		MaxAttempts = maxAttempts,
		InitialDelay = TimeSpan.Zero,
		BackoffFactor = 1.0,
		MaxDelay = TimeSpan.Zero,
	};

	private static byte[] DummyPng() => new byte[] { 0x89, 0x50, 0x4E, 0x47 };

	[Fact(DisplayName = "FetchKyoshinImageAsync: 初回成功時はリトライしない")]
	public async Task 初回成功時はリトライしない()
	{
		var handler = new StubHandler(new[]
		{
			new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(DummyPng()) },
		});
		var client = new HttpClient(handler);
		var builder = new ReplayFileBuilder(client, NullLogger<ReplayFileBuilder>.Instance, null, FastOptions(3));

		var result = await builder.FetchKyoshinImageAsync(new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc));

		Assert.NotNull(result);
		Assert.Equal(1, handler.CallCount);
	}

	[Fact(DisplayName = "FetchKyoshinImageAsync: 失敗後の再試行で成功する")]
	public async Task 失敗後の再試行で成功する()
	{
		var handler = new StubHandler(new[]
		{
			new HttpResponseMessage(HttpStatusCode.NotFound),
			new HttpResponseMessage(HttpStatusCode.NotFound),
			new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(DummyPng()) },
		});
		var client = new HttpClient(handler);
		var builder = new ReplayFileBuilder(client, NullLogger<ReplayFileBuilder>.Instance, null, FastOptions(5));

		var result = await builder.FetchKyoshinImageAsync(new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc));

		Assert.NotNull(result);
		Assert.Equal(3, handler.CallCount);
	}

	[Fact(DisplayName = "FetchKyoshinImageAsync: リトライ上限到達時に null を返す")]
	public async Task リトライ上限到達時にnullを返す()
	{
		var handler = new StubHandler(new[]
		{
			new HttpResponseMessage(HttpStatusCode.NotFound),
			new HttpResponseMessage(HttpStatusCode.NotFound),
			new HttpResponseMessage(HttpStatusCode.NotFound),
		});
		var client = new HttpClient(handler);
		var builder = new ReplayFileBuilder(client, NullLogger<ReplayFileBuilder>.Instance, null, FastOptions(3));

		var result = await builder.FetchKyoshinImageAsync(new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc));

		Assert.Null(result);
		Assert.Equal(3, handler.CallCount);
	}

	[Fact(DisplayName = "FetchKyoshinImageAsync: MaxAttempts=1 のときリトライしない")]
	public async Task MaxAttempts1のときリトライしない()
	{
		var handler = new StubHandler(new[]
		{
			new HttpResponseMessage(HttpStatusCode.NotFound),
		});
		var client = new HttpClient(handler);
		var builder = new ReplayFileBuilder(client, NullLogger<ReplayFileBuilder>.Instance, null, FastOptions(1));

		var result = await builder.FetchKyoshinImageAsync(new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc));

		Assert.Null(result);
		Assert.Equal(1, handler.CallCount);
	}
}
