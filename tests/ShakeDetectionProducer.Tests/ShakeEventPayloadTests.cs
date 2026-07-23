using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Core.Models.KyoshinMonitorObservationPoint;
using KyoshinMonitorLib;
using ShakeDetectionProducer;
using ShakeDetectionProducer.Services;
using System;
using Xunit;

namespace ShakeDetectionProducer.Tests;

public class ShakeEventPayloadTests
{
	[Fact(DisplayName = "producerの正本時刻と統合履歴を完全なペイロードへ変換する")]
	public void FromEvent_ProducerStateIsPreserved()
	{
		var createdAt = new DateTime(2026, 7, 23, 12, 0, 0, DateTimeKind.Utc);
		var updatedAt = createdAt.AddSeconds(5);
		var mergedAt = createdAt.AddSeconds(8);
		var firstPoint = CreatePoint("A", 35.123456f, 139.987654f, 2.4);
		var addedPoint = CreatePoint("B", 34.123456f, 140.987654f, 3.4);
		var mergedPoint = CreatePoint("C", 36.123456f, 138.987654f, 1.4);
		var canonical = new KyoshinEvent(createdAt, firstPoint, 10) { IsConfirmed = true };
		var merged = new KyoshinEvent(createdAt.AddSeconds(1), mergedPoint, 20) { IsConfirmed = true };

		canonical.AddPoint(addedPoint, updatedAt, 30);
		canonical.MergeEvent(merged, mergedAt);

		var payload = ShakeDetectedPayload.FromEvent(
			canonical,
			EventChangeReason.PointStateChanged |
			EventChangeReason.ExpiresAtExtended |
			EventChangeReason.EventsMerged,
			false
		);

		Assert.Equal(createdAt, payload.CreatedAt);
		Assert.Equal(mergedAt, payload.UpdatedAt);
		Assert.Equal(updatedAt.AddSeconds(30), payload.ExpiresAt);
		Assert.Equal(
			["point_state_changed", "expires_at_extended", "events_merged"],
			payload.ChangeReasons
		);
		var mergedEvent = Assert.Single(payload.MergedEvents);
		Assert.Equal(merged.Id, mergedEvent.EventId);
		Assert.Equal(mergedAt, mergedEvent.MergedAt);
		Assert.Equal(3, payload.PointCount);
		Assert.Equal(3, payload.Points.Length);
	}

	private static RealtimeObservationPoint CreatePoint(
		string code,
		float latitude,
		float longitude,
		double intensity
	)
	{
		var point = new RealtimeObservationPoint(new ObservationPointV2
		{
			Code = code,
			Name = $"観測点{code}",
			Region = "関東",
			SubRegion = "東京",
			Type = ObservationPointType.K_NET,
			Location = new Location(latitude, longitude),
			Point = new KyoshinImagePoint(new(), new()),
		});
		point.LatestIntensity = intensity;
		return point;
	}
}
