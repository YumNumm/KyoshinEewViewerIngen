using System.Text.Json;
using ShakeDetectionProducer.Services;

namespace ShakeDetectionProducer.Tests;

public class ShakeEventPublisherTests
{
	[Fact]
	public async Task PublishAsync_SendFailureRetriesIdenticalPayloadBeforeCommittingRevision()
	{
		var now = new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);
		var evt = TestEventFactory.CreateEvent(now);
		var tracker = new ShakeEventTracker();
		var attempts = new List<ShakeDetectedPayload>();
		var serializedAttempts = new List<byte[]>();
		var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

		async Task SendAsync(ShakeDetectedPayload payload)
		{
			attempts.Add(payload);
			serializedAttempts.Add(JsonSerializer.SerializeToUtf8Bytes<StreamPayload>(payload, options));
			await Task.Yield();
			if (attempts.Count == 1)
				throw new InvalidOperationException("Valkey unavailable");
		}

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => ShakeEventPublisher.PublishAsync(tracker, evt, now, SendAsync));
		var retry = await ShakeEventPublisher.PublishAsync(tracker, evt, now.AddSeconds(1), SendAsync);

		Assert.Same(attempts[0], attempts[1]);
		Assert.Same(attempts[1], retry);
		Assert.Equal(1, attempts[0].SerialNo);
		Assert.Equal(serializedAttempts[0], serializedAttempts[1]);

		evt.Level = KyoshinEewViewer.Core.Models.KyoshinEventLevel.Strong;
		var next = await ShakeEventPublisher.PublishAsync(tracker, evt, now.AddSeconds(2), SendAsync);

		Assert.NotNull(next);
		Assert.Equal(2, next.SerialNo);
		Assert.Contains("level_up", next.ChangeReasons);
	}
}
