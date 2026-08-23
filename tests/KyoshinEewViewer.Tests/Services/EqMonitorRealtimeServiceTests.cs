using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Threading.Channels;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Services.EqMonitor;
using Moq;
using Splat;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Tests.Services;

public class EqMonitorRealtimeServiceTests
{
	private const string EewEnvelope = """
		{"type":"realtime","data":{"type":"eew","operation":"upsert",
		"event_id":"20251212191438","record":{
		"event_id":"20251212191438","type":"VXSE45","status":"NORMAL",
		"info_type":"PUBLICATION","serial_no":3,"headline":null,
		"is_canceled":false,"is_warning":true,"is_last_info":false,
		"origin_time":"2025-12-12T19:14:38+09:00","arrival_time":null,
		"accuracy":null,"is_plum":false,"editorial_office":"仙台管区気象台",
		"report_time":"2025-12-12T19:14:50+09:00"}}}
		""";

	private sealed record FakeFrame(string? Text);

	private sealed class FakeWebSocket(bool closeImmediately = false) : IEqMonitorWebSocket
	{
		private readonly Channel<FakeFrame> _frames = Channel.CreateUnbounded<FakeFrame>();

		public TaskCompletionSource Connected { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource ReceiveCancelled { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		public ConcurrentQueue<string> SentMessages { get; } = [];
		public Dictionary<string, string> Headers { get; } = [];
		public Uri? Endpoint { get; private set; }

		public Task ConnectAsync(
			Uri endpoint,
			IReadOnlyDictionary<string, string> headers,
			CancellationToken cancellationToken)
		{
			Endpoint = endpoint;
			foreach (var header in headers)
				Headers[header.Key] = header.Value;
			if (closeImmediately)
				_frames.Writer.TryWrite(new FakeFrame(null));
			Connected.TrySetResult();
			return Task.CompletedTask;
		}

		public async Task<string?> ReceiveTextAsync(CancellationToken cancellationToken)
		{
			try
			{
				return (await _frames.Reader.ReadAsync(cancellationToken)).Text;
			}
			catch (OperationCanceledException)
			{
				ReceiveCancelled.TrySetResult();
				throw;
			}
		}

		public Task SendTextAsync(string text, CancellationToken cancellationToken)
		{
			SentMessages.Enqueue(text);
			return Task.CompletedTask;
		}

		public Task CloseAsync(CancellationToken cancellationToken)
		{
			_frames.Writer.TryWrite(new FakeFrame(null));
			return Task.CompletedTask;
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;

		public void Receive(string json)
			=> _frames.Writer.TryWrite(new FakeFrame(json));
	}

	private sealed class ApiHandler : HttpMessageHandler
	{
		public int RegisterCount { get; private set; }
		public int TicketCount { get; private set; }
		public bool RejectFirstTicket { get; init; }

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			if (request.RequestUri!.AbsolutePath == "/v2/device")
			{
				RegisterCount++;
				return Json(HttpStatusCode.Created,
					$$"""{"deviceId":"device-{{RegisterCount}}","deviceToken":"discard","expiresAt":null}""");
			}

			Assert.Equal("/v2/realtime/ticket", request.RequestUri.AbsolutePath);
			TicketCount++;
			if (RejectFirstTicket && TicketCount == 1)
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
				{
					Content = new StringContent("invalid device"),
				});
			return Json(HttpStatusCode.OK,
				"""{"url":"wss://socket.invalid/realtime?ticket=secret","expiresAt":"2026-08-23T01:00:00Z","issuedAt":"2026-08-23T00:00:00Z"}""");
		}

		private static Task<HttpResponseMessage> Json(HttpStatusCode status, string body)
			=> Task.FromResult(new HttpResponseMessage(status)
			{
				Content = new StringContent(body, Encoding.UTF8, "application/json"),
			});
	}

	[Fact(DisplayName = "1本のWebSocketでready同期後にイベントを適用しpingへ即時応答する")]
	public async Task 単一接続と順序制御()
	{
		var config = CreateConfig();
		var handler = new ApiHandler();
		using var provider = CreateProvider(config, handler);
		var fake = new FakeWebSocket();
		var socketCount = 0;
		using var service = new EqMonitorRealtimeService(CreateLogManager(), config, provider)
		{
			CreateWebSocket = () =>
			{
				socketCount++;
				return fake;
			},
		};
		service.ReplaceDeviceServiceForTesting(new EqMonitorDeviceService(provider, config, _ => { }));
		var order = new ConcurrentQueue<string>();
		var releaseSync = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var syncStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var eventApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		service.RegisterEewConsumer(
			async _ =>
			{
				order.Enqueue("eew-sync");
				syncStarted.TrySetResult();
				await releaseSync.Task;
			},
			_ =>
			{
				order.Enqueue("eew-event");
				eventApplied.TrySetResult();
			},
			_ => { });
		service.RegisterEarthquakeConsumer(
			_ =>
			{
				order.Enqueue("earthquake-sync");
				return Task.CompletedTask;
			},
			_ => order.Enqueue("earthquake-event"),
			_ => { });

		service.Start();
		await fake.Connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
		fake.Receive("""{"type":"ready"}""");
		await syncStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		fake.Receive("""{"type":"ping"}""");
		fake.Receive("{");
		fake.Receive(EewEnvelope);

		await WaitUntilAsync(() => fake.SentMessages.Contains("""{"type":"pong"}"""));
		Assert.False(eventApplied.Task.IsCompleted);
		releaseSync.TrySetResult();
		await eventApplied.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Equal(["eew-sync", "earthquake-sync", "eew-event"], order);
		Assert.Equal(1, socketCount);
		Assert.Equal(EqMonitorApiProvider.UserAgent, fake.Headers["User-Agent"]);
		Assert.Equal(EqMonitorApiProvider.BuildNumber, fake.Headers["x-eqmonitor-build"]);
	}

	[Fact(DisplayName = "ready前の切断は1秒から60秒上限で指数バックオフする")]
	public async Task 再接続バックオフ()
	{
		var config = CreateConfig();
		var handler = new ApiHandler();
		using var provider = CreateProvider(config, handler);
		using var service = new EqMonitorRealtimeService(CreateLogManager(), config, provider)
		{
			CreateWebSocket = () => new FakeWebSocket(closeImmediately: true),
		};
		service.ReplaceDeviceServiceForTesting(new EqMonitorDeviceService(provider, config, _ => { }));
		var delays = new List<TimeSpan>();
		var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		service.DelayAsync = (delay, _) =>
		{
			delays.Add(delay);
			if (delays.Count == 7)
			{
				completed.TrySetResult();
				config.EqMonitor.Enable = false;
			}
			return Task.CompletedTask;
		};
		service.RegisterEewConsumer(_ => Task.CompletedTask, _ => { }, _ => { });

		service.Start();
		await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Equal(
			[1, 2, 4, 8, 16, 32, 60],
			delays.Select(delay => (int)delay.TotalSeconds));
	}

	[Fact(DisplayName = "ready後に登録した購読者へ接続済みを通知してから初期同期する")]
	public async Task Ready後の購読者登録()
	{
		var config = CreateConfig();
		var handler = new ApiHandler();
		using var provider = CreateProvider(config, handler);
		var fake = new FakeWebSocket();
		using var service = new EqMonitorRealtimeService(CreateLogManager(), config, provider)
		{
			CreateWebSocket = () => fake,
		};
		service.ReplaceDeviceServiceForTesting(new EqMonitorDeviceService(provider, config, _ => { }));
		var firstSync = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		service.RegisterEewConsumer(
			_ =>
			{
				firstSync.TrySetResult();
				return Task.CompletedTask;
			},
			_ => { },
			_ => { });

		service.Start();
		await fake.Connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
		fake.Receive("""{"type":"ready"}""");
		await firstSync.Task.WaitAsync(TimeSpan.FromSeconds(5));

		var order = new ConcurrentQueue<string>();
		var lateSync = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		service.RegisterEarthquakeConsumer(
			_ =>
			{
				order.Enqueue("sync");
				lateSync.TrySetResult();
				return Task.CompletedTask;
			},
			_ => { },
			connected => order.Enqueue($"connected:{connected}"));

		await lateSync.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Equal(["connected:True", "sync"], order);
	}

	[Fact(DisplayName = "無効端末IDのチケット応答では一度だけ再登録して待機なしで接続する")]
	public async Task 無効端末IDの再登録()
	{
		var config = CreateConfig();
		config.EqMonitor.DeviceId = "stale-device";
		config.EqMonitor.DeviceRegisteredBaseUrl = "https://api.invalid/";
		var handler = new ApiHandler { RejectFirstTicket = true };
		using var provider = CreateProvider(config, handler);
		var saveCount = 0;
		var fake = new FakeWebSocket();
		using var service = new EqMonitorRealtimeService(CreateLogManager(), config, provider)
		{
			CreateWebSocket = () => fake,
		};
		service.ReplaceDeviceServiceForTesting(
			new EqMonitorDeviceService(provider, config, _ => saveCount++));
		var delayCount = 0;
		service.DelayAsync = (_, _) =>
		{
			delayCount++;
			return Task.CompletedTask;
		};
		service.RegisterEewConsumer(_ => Task.CompletedTask, _ => { }, _ => { });

		service.Start();
		await fake.Connected.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Equal(2, handler.TicketCount);
		Assert.Equal(1, handler.RegisterCount);
		Assert.Equal(2, saveCount);
		Assert.Equal(0, delayCount);
		config.EqMonitor.Enable = false;
		await fake.ReceiveCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
	}

	private static KyoshinEewViewerConfiguration CreateConfig()
	{
		var config = new KyoshinEewViewerConfiguration();
		config.EqMonitor.BaseUrl = "https://api.invalid";
		config.EqMonitor.Enable = true;
		config.EqMonitor.EnableEew = true;
		config.EqMonitor.EnableEarthquake = true;
		return config;
	}

	private static EqMonitorApiProvider CreateProvider(
		KyoshinEewViewerConfiguration config,
		HttpMessageHandler handler)
		=> new(CreateLogManager(), config)
		{
			CreateHttpClient = () => new HttpClient(handler, disposeHandler: false),
		};

	private static ILogManager CreateLogManager()
	{
		var logManager = new Mock<ILogManager>();
		logManager.Setup(x => x.GetLogger(It.IsAny<Type>()))
			.Returns(new Mock<IFullLogger>().Object);
		return logManager.Object;
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		while (!condition())
			await Task.Delay(10, timeout.Token);
	}
}
