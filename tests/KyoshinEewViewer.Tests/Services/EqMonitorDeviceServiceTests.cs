using System.Net;
using System.Text;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Services.EqMonitor;
using Microsoft.Extensions.Logging;
using Moq;

namespace KyoshinEewViewer.Tests.Services;

public class EqMonitorDeviceServiceTests
{
	private sealed class RegistrationHandler(params string[] deviceIds) : HttpMessageHandler
	{
		private readonly Queue<string> _deviceIds = new(deviceIds);

		public List<string> RequestBodies { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			Assert.Equal(HttpMethod.Post, request.Method);
			Assert.Equal("/v2/device", request.RequestUri!.AbsolutePath);
			RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));

			var deviceId = _deviceIds.Dequeue();
			return new HttpResponseMessage(HttpStatusCode.Created)
			{
				Content = new StringContent(
					$$"""{"deviceId":"{{deviceId}}","deviceToken":"破棄するトークン","expiresAt":null}""",
					Encoding.UTF8,
					"application/json"),
			};
		}
	}

	[Fact(DisplayName = "端末IDを接続先ごとに再利用し、変更・無効化時はDesktopとして再登録する")]
	public async Task 端末IDの登録と再利用()
	{
		var config = new KyoshinEewViewerConfiguration();
		config.EqMonitor.BaseUrl = "https://api-a.invalid";
		var handler = new RegistrationHandler("device-a", "device-b", "device-c");
		using var provider = new EqMonitorApiProvider(CreateLogger(), config)
		{
			CreateHttpClient = () => new HttpClient(handler, disposeHandler: false),
		};
		var saveCount = 0;
		using var service = new EqMonitorDeviceService(provider, config, _ => saveCount++);

		Assert.Equal("device-a", await service.GetOrRegisterDeviceIdAsync(CancellationToken.None));
		Assert.Equal("device-a", await service.GetOrRegisterDeviceIdAsync(CancellationToken.None));
		Assert.Single(handler.RequestBodies);
		Assert.Equal(1, saveCount);
		Assert.Equal("https://api-a.invalid/", config.EqMonitor.DeviceRegisteredBaseUrl);

		config.EqMonitor.BaseUrl = "https://api-b.invalid";
		Assert.Equal("device-b", await service.GetOrRegisterDeviceIdAsync(CancellationToken.None));
		Assert.Equal("https://api-b.invalid/", config.EqMonitor.DeviceRegisteredBaseUrl);

		service.InvalidateRegistration();
		Assert.Null(config.EqMonitor.DeviceId);
		Assert.Null(config.EqMonitor.DeviceRegisteredBaseUrl);
		Assert.Equal("device-c", await service.GetOrRegisterDeviceIdAsync(CancellationToken.None));

		Assert.Equal(3, handler.RequestBodies.Count);
		Assert.All(handler.RequestBodies, body =>
			Assert.Equal("{\"type\":\"DESKTOP\",\"locale\":\"ja\"}", body));
		Assert.Equal(4, saveCount);
	}

	private static ILogger<EqMonitorApiProvider> CreateLogger()
		=> new Mock<ILogger<EqMonitorApiProvider>>().Object;
}
