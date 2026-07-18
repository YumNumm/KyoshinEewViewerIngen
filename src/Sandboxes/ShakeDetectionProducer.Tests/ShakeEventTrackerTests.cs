using ShakeDetectionProducer.Services;

namespace ShakeDetectionProducer.Tests;

public class ShakeEventTrackerTests
{
	[Fact]
	public void SemanticChanges_AreSentImmediatelyWithConsecutiveSerials()
	{
		var now = new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);
		var evt = TestEventFactory.CreateEvent(now);
		var tracker = new ShakeEventTracker();

		var first = Assert.IsType<ShakeDetectedPayload>(tracker.PrepareEvent(evt, now));
		tracker.Commit(first, now);

		evt.Points[0].Update(null, 2.0);
		var second = Assert.IsType<ShakeDetectedPayload>(tracker.PrepareEvent(evt, now.AddSeconds(1)));
		tracker.Commit(second, now.AddSeconds(1));

		evt.AddPoint(TestEventFactory.CreatePoint("POINT-B", 1.5, 36, 140), now.AddSeconds(2), 10);
		var third = Assert.IsType<ShakeDetectedPayload>(tracker.PrepareEvent(evt, now.AddSeconds(2)));

		Assert.Equal(new[] { 1, 2, 3 }, new[] { first.SerialNo, second.SerialNo, third.SerialNo });
		Assert.Contains("point_state_changed", second.ChangeReasons);
		Assert.Contains("points_changed", third.ChangeReasons);
	}

	[Fact]
	public void ExpiryOnlyChange_IsNotSentBeforeFiveSeconds()
	{
		var now = new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);
		var evt = TestEventFactory.CreateEvent(now);
		var tracker = new ShakeEventTracker();
		var first = Assert.IsType<ShakeDetectedPayload>(tracker.PrepareEvent(evt, now));
		tracker.Commit(first, now);

		evt.Points[0].EventedExpireAt = evt.Points[0].EventedExpireAt.AddSeconds(10);

		Assert.Null(tracker.PrepareEvent(evt, now.AddMilliseconds(4999)));
	}

	[Fact]
	public void ExpiryOnlyChange_IsSentAtFiveSeconds()
	{
		var now = new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);
		var evt = TestEventFactory.CreateEvent(now);
		var tracker = new ShakeEventTracker();
		var first = Assert.IsType<ShakeDetectedPayload>(tracker.PrepareEvent(evt, now));
		tracker.Commit(first, now);

		evt.Points[0].EventedExpireAt = evt.Points[0].EventedExpireAt.AddSeconds(10);
		var heartbeat = Assert.IsType<ShakeDetectedPayload>(tracker.PrepareEvent(evt, now.AddSeconds(5)));

		Assert.Equal(2, heartbeat.SerialNo);
		Assert.Equal(new[] { "expires_at_extended" }, heartbeat.ChangeReasons);
	}

	[Fact]
	public void FailedSend_RetriesIdenticalPendingPayloadBeforeAllocatingNextSerial()
	{
		var now = new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);
		var evt = TestEventFactory.CreateEvent(now);
		var tracker = new ShakeEventTracker();

		var failedAttempt = Assert.IsType<ShakeDetectedPayload>(tracker.PrepareEvent(evt, now));
		evt.Level = KyoshinEewViewer.Core.Models.KyoshinEventLevel.Strong;
		var retry = Assert.IsType<ShakeDetectedPayload>(tracker.PrepareEvent(evt, now.AddSeconds(1)));

		Assert.Same(failedAttempt, retry);
		Assert.Equal(failedAttempt.SerialNo, retry.SerialNo);
		tracker.Commit(retry, now.AddSeconds(1));

		var next = Assert.IsType<ShakeDetectedPayload>(tracker.PrepareEvent(evt, now.AddSeconds(2)));
		Assert.Equal(2, next.SerialNo);
		Assert.Contains("level_up", next.ChangeReasons);
	}

	[Fact]
	public void LevelChanges_AreSentImmediatelyWithDirectionalReasons()
	{
		var now = new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);
		var evt = TestEventFactory.CreateEvent(now);
		var tracker = new ShakeEventTracker();
		var first = Assert.IsType<ShakeDetectedPayload>(tracker.PrepareEvent(evt, now));
		tracker.Commit(first, now);

		evt.Level = KyoshinEewViewer.Core.Models.KyoshinEventLevel.Strong;
		var levelUp = Assert.IsType<ShakeDetectedPayload>(tracker.PrepareEvent(evt, now.AddMilliseconds(1)));
		Assert.Contains("level_up", levelUp.ChangeReasons);
		tracker.Commit(levelUp, now.AddMilliseconds(1));

		evt.Level = KyoshinEewViewer.Core.Models.KyoshinEventLevel.Weak;
		var levelDown = Assert.IsType<ShakeDetectedPayload>(tracker.PrepareEvent(evt, now.AddMilliseconds(2)));
		Assert.Contains("level_down", levelDown.ChangeReasons);
	}

	[Fact]
	public void BoundsChange_IsSentImmediatelyWithRegionReason()
	{
		var now = new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);
		var evt = TestEventFactory.CreateEvent(now);
		var tracker = new ShakeEventTracker();
		var first = Assert.IsType<ShakeDetectedPayload>(tracker.PrepareEvent(evt, now));
		tracker.Commit(first, now);

		evt.TopLeft.Latitude -= 1;
		var changed = Assert.IsType<ShakeDetectedPayload>(tracker.PrepareEvent(evt, now.AddMilliseconds(1)));

		Assert.Contains("region_changed", changed.ChangeReasons);
	}
}
