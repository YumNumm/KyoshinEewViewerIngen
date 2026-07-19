using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Core.Models.KyoshinMonitorObservationPoint;
using KyoshinMonitorLib;

namespace ShakeDetectionProducer.Tests;

internal static class TestEventFactory
{
	public static KyoshinEvent CreateEvent(
		DateTime createdAt,
		string code = "POINT-A",
		double intensity = 1.0,
		int expireSeconds = 10,
		float latitude = 35,
		float longitude = 139)
	{
		var point = CreatePoint(code, intensity, latitude, longitude);
		return new KyoshinEvent(createdAt, point, expireSeconds) { IsConfirmed = true };
	}

	public static RealtimeObservationPoint CreatePoint(
		string code,
		double intensity,
		float latitude = 35,
		float longitude = 139)
	{
		var point = new RealtimeObservationPoint(new ObservationPointV2
		{
			Code = code,
			Name = $"Name-{code}",
			Region = "Region",
			SubRegion = "SubRegion",
			Type = ObservationPointType.K_NET,
			Location = new Location(latitude, longitude),
			Point = new(new(), new()),
		});
		point.Update(null, intensity);
		return point;
	}
}
