using KyoshinEewViewer.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ShakeDetectionProducer.Services;

/// <summary>
/// イベントの変化理由
/// </summary>
[Flags]
public enum EventChangeReason
{
	None = 0,
	NewEvent = 1 << 0,
	LevelUp = 1 << 1,
	LevelDown = 1 << 2,
	RegionChanged = 1 << 3,
	PointsChanged = 1 << 4,
	PointStateChanged = 1 << 5,
	ExpiresAtExtended = 1 << 6,
	EventsMerged = 1 << 7,
}

internal readonly record struct EventPointState(double? Intensity, double IntensityDiff);

/// <summary>
/// 送信済み、または送信待ちのイベント状態のスナップショット
/// </summary>
internal record EventStateSnapshot
{
	public required KyoshinEventLevel Level { get; init; }
	public required float TopLatitude { get; init; }
	public required float TopLongitude { get; init; }
	public required float BottomLatitude { get; init; }
	public required float BottomLongitude { get; init; }
	public required Dictionary<string, EventPointState> Points { get; init; }
	public required HashSet<Guid> MergedEventIds { get; init; }
	public required DateTime ExpiresAt { get; init; }

	public static EventStateSnapshot FromEvent(KyoshinEvent evt)
		=> new()
		{
			Level = evt.Level,
			TopLatitude = evt.TopLeft.Latitude,
			TopLongitude = evt.TopLeft.Longitude,
			BottomLatitude = evt.BottomRight.Latitude,
			BottomLongitude = evt.BottomRight.Longitude,
			Points = evt.Points.ToDictionary(
				p => p.Code,
				p => new EventPointState(p.LatestIntensity, p.IntensityDiff)),
			MergedEventIds = evt.MergedEvents.Select(e => e.EventId).ToHashSet(),
			ExpiresAt = evt.ExpiresAt,
		};
}

internal record EventCacheEntry
{
	public required EventStateSnapshot State { get; init; }
	public required int SerialNo { get; init; }
	public required DateTime LastSentAt { get; init; }
}

internal record PendingEventEntry
{
	public required EventStateSnapshot State { get; init; }
	public required ShakeDetectedPayload Payload { get; init; }
}

/// <summary>
/// 揺れイベントの送信済み状態と送信待ち revision を追跡するトラッカー
/// </summary>
public class ShakeEventTracker
{
	private static readonly TimeSpan ExpiryHeartbeatInterval = TimeSpan.FromSeconds(5);

	private Dictionary<Guid, EventCacheEntry> EventCache { get; } = [];
	private Dictionary<Guid, PendingEventEntry> PendingEvents { get; } = [];

	/// <summary>
	/// 現在のイベントから次の送信 payload を準備する。
	/// 送信待ちがある場合は、成功するまで同一 payload を返す。
	/// </summary>
	public ShakeDetectedPayload? PrepareEvent(KyoshinEvent evt, DateTime observedAt)
	{
		if (PendingEvents.TryGetValue(evt.Id, out var pending))
			return pending.Payload;

		var current = EventStateSnapshot.FromEvent(evt);
		EventChangeReason changeReason;
		int serialNo;

		if (!EventCache.TryGetValue(evt.Id, out var cached))
		{
			changeReason = EventChangeReason.NewEvent;
			serialNo = 1;
		}
		else
		{
			changeReason = Compare(current, cached.State);
			if (changeReason == EventChangeReason.None)
				return null;

			var expiryOnly = changeReason == EventChangeReason.ExpiresAtExtended;
			if (expiryOnly && observedAt - cached.LastSentAt < ExpiryHeartbeatInterval)
				return null;

			serialNo = cached.SerialNo + 1;
		}

		var payload = ShakeDetectedPayload.FromEvent(evt, serialNo, observedAt, changeReason);
		PendingEvents[evt.Id] = new PendingEventEntry
		{
			State = current,
			Payload = payload,
		};
		return payload;
	}

	/// <summary>
	/// Valkey への送信成功後にだけ prepared revision を送信済みとして確定する。
	/// </summary>
	public void Commit(ShakeDetectedPayload payload, DateTime sentAt)
	{
		if (!PendingEvents.TryGetValue(payload.EventId, out var pending) ||
			!ReferenceEquals(pending.Payload, payload))
			throw new InvalidOperationException("送信待ちではない揺れ検知 payload は確定できません。");

		EventCache[payload.EventId] = new EventCacheEntry
		{
			State = pending.State,
			SerialNo = payload.SerialNo,
			LastSentAt = sentAt,
		};
		PendingEvents.Remove(payload.EventId);
	}

	private static EventChangeReason Compare(EventStateSnapshot current, EventStateSnapshot cached)
	{
		var reason = EventChangeReason.None;

		if (current.Level > cached.Level)
			reason |= EventChangeReason.LevelUp;
		else if (current.Level < cached.Level)
			reason |= EventChangeReason.LevelDown;

		if (!IsCoordinateEqual(current.TopLatitude, cached.TopLatitude) ||
			!IsCoordinateEqual(current.TopLongitude, cached.TopLongitude) ||
			!IsCoordinateEqual(current.BottomLatitude, cached.BottomLatitude) ||
			!IsCoordinateEqual(current.BottomLongitude, cached.BottomLongitude))
			reason |= EventChangeReason.RegionChanged;

		var currentCodes = current.Points.Keys.ToHashSet();
		var cachedCodes = cached.Points.Keys.ToHashSet();
		if (!currentCodes.SetEquals(cachedCodes))
			reason |= EventChangeReason.PointsChanged;

		if (currentCodes.Intersect(cachedCodes).Any(code => current.Points[code] != cached.Points[code]))
			reason |= EventChangeReason.PointStateChanged;

		if (!current.MergedEventIds.SetEquals(cached.MergedEventIds))
			reason |= EventChangeReason.EventsMerged;

		if (current.ExpiresAt > cached.ExpiresAt)
			reason |= EventChangeReason.ExpiresAtExtended;

		return reason;
	}

	private static bool IsCoordinateEqual(float a, float b)
	{
		const float tolerance = 0.0001f;
		return Math.Abs(a - b) < tolerance;
	}

	public void CleanupCache(KyoshinEvent[] currentEvents)
	{
		var currentEventIds = currentEvents.Select(e => e.Id).ToHashSet();
		foreach (var key in EventCache.Keys.Concat(PendingEvents.Keys).Distinct().ToArray())
		{
			if (currentEventIds.Contains(key))
				continue;
			EventCache.Remove(key);
			PendingEvents.Remove(key);
		}
	}

	public void Clear()
	{
		EventCache.Clear();
		PendingEvents.Clear();
	}
}
