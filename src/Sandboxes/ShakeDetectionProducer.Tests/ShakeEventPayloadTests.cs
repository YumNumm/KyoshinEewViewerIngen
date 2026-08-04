using System.Text.Json;
using KyoshinEewViewer.Core.Models;
using ShakeDetectionProducer.Services;

namespace ShakeDetectionProducer.Tests;

public class ShakeEventPayloadTests
{
	[Fact(DisplayName = "観測点状態の変化はtracker理由とupdatedAtを同時に進める")]
	public void PointStateLifecycle_AdvancesUpdatedAt()
	{
		var createdAt = new DateTime(2026, 7, 23, 12, 0, 0, DateTimeKind.Utc);
		var changedAt = createdAt.AddSeconds(1);
		var point = TestEventFactory.CreatePoint("A", 1.2);
		var evt = new KyoshinEvent(createdAt, point, 10) { IsConfirmed = true };
		var tracker = new ShakeEventTracker();
		var initial = Assert.IsType<ShakeDetectedPayload>(tracker.PrepareEvent(evt, createdAt));
		tracker.Commit(initial, createdAt);

		point.LatestIntensity = 2.2;
		evt.UpdatePointState(point, changedAt);
		var payload = Assert.IsType<ShakeDetectedPayload>(
			tracker.PrepareEvent(evt, changedAt.AddSeconds(5)));

		Assert.Contains("point_state_changed", payload.ChangeReasons);
		Assert.Equal(changedAt, payload.UpdatedAt);
	}

	[Fact(DisplayName = "trackerは可変regionをsnapshotして範囲変更を検出する")]
	public void RegionLifecycle_DetectsMutableBoundsChange()
	{
		var createdAt = new DateTime(2026, 7, 23, 12, 0, 0, DateTimeKind.Utc);
		var evt = TestEventFactory.CreateEvent(createdAt, "A", 1.2);
		var tracker = new ShakeEventTracker();
		var initial = Assert.IsType<ShakeDetectedPayload>(tracker.PrepareEvent(evt, createdAt));
		tracker.Commit(initial, createdAt);

		evt.AddPoint(
			TestEventFactory.CreatePoint("B", 1.3, 34, 140),
			createdAt.AddSeconds(1),
			10);
		var payload = Assert.IsType<ShakeDetectedPayload>(
			tracker.PrepareEvent(evt, createdAt.AddSeconds(1)));

		Assert.Contains("region_changed", payload.ChangeReasons);
	}

	[Fact(DisplayName = "producerの正本時刻と統合履歴を完全なペイロードへ変換する")]
	public void FromEvent_ProducerStateIsPreserved()
	{
		var createdAt = new DateTime(2026, 7, 23, 12, 0, 0, DateTimeKind.Utc);
		var updatedAt = createdAt.AddSeconds(5);
		var mergedAt = createdAt.AddSeconds(8);
		var firstPoint = TestEventFactory.CreatePoint("A", 2.4, 35.123456f, 139.987654f);
		var addedPoint = TestEventFactory.CreatePoint("B", 3.4, 34.123456f, 140.987654f);
		var mergedPoint = TestEventFactory.CreatePoint("C", 1.4, 36.123456f, 138.987654f);
		var canonical = new KyoshinEvent(createdAt, firstPoint, 10) { IsConfirmed = true };
		var merged = new KyoshinEvent(createdAt.AddSeconds(1), mergedPoint, 20) { IsConfirmed = true };

		canonical.AddPoint(addedPoint, updatedAt, 30);
		canonical.MergeEvent(merged, mergedAt);

		var payload = ShakeDetectedPayload.FromEvent(
			canonical,
			1,
			EventChangeReason.PointStateChanged |
			EventChangeReason.ExpiresAtExtended |
			EventChangeReason.EventsMerged);

		Assert.Equal(createdAt, payload.CreatedAt);
		Assert.Equal(mergedAt, payload.UpdatedAt);
		Assert.Equal(updatedAt.AddSeconds(30), payload.ExpiresAt);
		Assert.Equal(
			["point_state_changed", "expires_at_extended", "events_merged"],
			payload.ChangeReasons);
		var mergedEvent = Assert.Single(payload.MergedEvents);
		Assert.Equal(merged.Id, mergedEvent.EventId);
		Assert.Equal(mergedAt, mergedEvent.MergedAt);
		Assert.Equal(3, payload.PointCount);
		Assert.Equal(3, payload.Points.Length);
	}

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
		Assert.Equal(
			new[] { now.AddSeconds(11), now.AddSeconds(10), now.AddSeconds(12) },
			a.MergedEvents.Select(x => x.MergedAt));
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
		var payload = Assert.IsType<ShakeDetectedPayload>(tracker.PrepareEvent(canonical, now.AddSeconds(5)));

		Assert.Equal(canonical.Points.Max(point => point.EventedExpireAt), payload.ExpiresAt);
		Assert.Equal(now.AddSeconds(2), payload.UpdatedAt);
		Assert.Contains("events_merged", payload.ChangeReasons);
		Assert.Equal(canonical.MergedEvents.Select(x => x.EventId), payload.MergedEvents.Select(x => x.EventId));

		var json = JsonSerializer.Serialize<StreamPayload>(payload, new JsonSerializerOptions
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		});
		using var document = JsonDocument.Parse(json);
		var root = document.RootElement;
		Assert.Equal(
			new[]
			{
				"changeReasons", "createdAt", "eventId", "expiresAt", "level", "mergedEvents",
				"pointCount", "points", "region", "serialNo", "type", "updatedAt",
			},
			root.EnumerateObject().Select(property => property.Name).Order());
		Assert.Equal("shake_detection", root.GetProperty("type").GetString());
		Assert.True(Guid.TryParse(root.GetProperty("eventId").GetString(), out _));
		Assert.Equal(2, root.GetProperty("serialNo").GetInt32());
		Assert.True(DateTime.TryParse(root.GetProperty("createdAt").GetString(), out _));
		Assert.True(DateTime.TryParse(root.GetProperty("updatedAt").GetString(), out _));
		Assert.True(DateTime.TryParse(root.GetProperty("expiresAt").GetString(), out _));
		Assert.Equal(JsonValueKind.String, root.GetProperty("level").ValueKind);
		Assert.Equal(JsonValueKind.Array, root.GetProperty("changeReasons").ValueKind);
		Assert.Equal(JsonValueKind.Array, root.GetProperty("mergedEvents").ValueKind);
		Assert.Equal(JsonValueKind.Number, root.GetProperty("pointCount").ValueKind);
		Assert.Equal(JsonValueKind.Object, root.GetProperty("region").ValueKind);
		Assert.Equal(JsonValueKind.Array, root.GetProperty("points").ValueKind);

		var mergedEvent = root.GetProperty("mergedEvents")[0];
		Assert.Equal(new[] { "eventId", "mergedAt" }, mergedEvent.EnumerateObject().Select(x => x.Name).Order());
		Assert.Equal(JsonValueKind.String, mergedEvent.GetProperty("eventId").ValueKind);
		Assert.Equal(JsonValueKind.String, mergedEvent.GetProperty("mergedAt").ValueKind);

		var region = root.GetProperty("region");
		Assert.Equal(new[] { "bottomRight", "topLeft" }, region.EnumerateObject().Select(x => x.Name).Order());
		Assert.All(
			new[] { region.GetProperty("topLeft"), region.GetProperty("bottomRight") },
			location => Assert.Equal(
				new[] { "latitude", "longitude" },
				location.EnumerateObject().Select(x => x.Name).Order()));

		var point = root.GetProperty("points")[0];
		Assert.Equal(
			new[] { "code", "intensity", "intensityDiff", "location", "name", "region", "type" },
			point.EnumerateObject().Select(x => x.Name).Order());
		Assert.Equal(JsonValueKind.String, point.GetProperty("code").ValueKind);
		Assert.Equal(JsonValueKind.String, point.GetProperty("name").ValueKind);
		Assert.Equal(JsonValueKind.String, point.GetProperty("region").ValueKind);
		Assert.Equal(JsonValueKind.String, point.GetProperty("type").ValueKind);
		Assert.Equal(JsonValueKind.Object, point.GetProperty("location").ValueKind);
		Assert.Equal(JsonValueKind.Number, point.GetProperty("intensity").ValueKind);
		Assert.Equal(JsonValueKind.Number, point.GetProperty("intensityDiff").ValueKind);
		Assert.False(root.TryGetProperty("isReplay", out _));
	}
}
