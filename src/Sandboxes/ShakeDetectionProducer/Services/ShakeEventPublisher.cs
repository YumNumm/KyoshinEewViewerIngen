using KyoshinEewViewer.Core.Models;
using System;
using System.Threading.Tasks;

namespace ShakeDetectionProducer.Services;

/// <summary>
/// 揺れ検知 payload の準備、送信、成功確定を順序どおりに実行する。
/// </summary>
public static class ShakeEventPublisher
{
	public static async Task<ShakeDetectedPayload?> PublishAsync(
		ShakeEventTracker tracker,
		KyoshinEvent evt,
		DateTime observedAt,
		Func<ShakeDetectedPayload, Task> publishAsync)
	{
		var payload = tracker.PrepareEvent(evt, observedAt);
		if (payload == null)
			return null;

		await publishAsync(payload);
		tracker.Commit(payload, observedAt);
		return payload;
	}
}
