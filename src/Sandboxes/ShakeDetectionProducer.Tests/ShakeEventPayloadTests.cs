using System.Text.Json;
using KyoshinEewViewer.Core.Models;
using ShakeDetectionProducer.Services;

namespace ShakeDetectionProducer.Tests;

public class ShakeEventPayloadTests
{
	[Fact]
	public void MergeEvent_PreservesUniqueDirectAndTransitiveLineage()
	{
		var now = new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);
		var a = TestEventFactory.CreateEvent(now, "A");
		var b = TestEventFactory.CreateEvent(now.AddSeconds(1), "B");
		var c = TestEventFactory.CreateEvent(now.AddSeconds(2), "C");
		var d = TestEventFactory.CreateEvent(now.AddSeconds(3), "D");

		b.MergeEvent(c, now.AddSeconds(10));
		a.MergeEvent(b, now.AddSeconds(11));
		a.MergeEvent(d, now.AddSeconds(12));
		a.MergeEvent(c, now.AddSeconds(13));

		Assert.Equal(new[] { b.Id, c.Id, d.Id }, a.MergedEvents.Select(x => x.EventId));
		Assert.DoesNotContain(a.MergedEvents, x => x.EventId == a.Id);
	}

	[Fact]
	public void Payload_ProjectsCanonicalSourceSchemaExpiryAndMergeReason()
	{
		var now = new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);
		var canonical = TestEventFactory.CreateEvent(now, "A");
		var tracker = new ShakeEventTracker();
		var first = Assert.IsType<ShakeDetectedPayload>(tracker.PrepareEvent(canonical, now));
		tracker.Commit(first, now);

		var merged = TestEventFactory.CreateEvent(now.AddSeconds(1), "B", expireSeconds: 30);
		canonical.MergeEvent(merged, now.AddSeconds(2));
		var payload = Assert.IsType<ShakeDetectedPayload>(tracker.PrepareEvent(canonical, now.AddSeconds(2)));

		Assert.Equal(canonical.Points.Max(point => point.EventedExpireAt), payload.ExpiresAt);
		Assert.Contains("events_merged", payload.ChangeReasons);
		Assert.Equal(canonical.MergedEvents.Select(x => x.EventId), payload.MergedEvents.Select(x => x.EventId));

		var json = JsonSerializer.Serialize<StreamPayload>(payload, new JsonSerializerOptions
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		});
		using var document = JsonDocument.Parse(json);
		var root = document.RootElement;
		Assert.Equal("shake_detection", root.GetProperty("type").GetString());
		Assert.Equal(2, root.GetProperty("serialNo").GetInt32());
		Assert.True(root.TryGetProperty("updatedAt", out _));
		Assert.True(root.TryGetProperty("expiresAt", out _));
		Assert.True(root.TryGetProperty("mergedEvents", out _));
		Assert.False(root.TryGetProperty("isReplay", out _));
	}
}
