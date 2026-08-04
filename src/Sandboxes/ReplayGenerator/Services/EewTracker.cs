using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ReplayGenerator.Infrastructure;
using ReplayGenerator.Models;

namespace ReplayGenerator.Services;

public class EewTracker
{
	private readonly ValkeyStateManager _state;
	private readonly ILogger<EewTracker> _logger;

	public EewTracker(ValkeyStateManager state, ILogger<EewTracker> logger)
	{
		_state = state;
		_logger = logger;
	}

	/// <summary>
	/// EEW最終報を受信した際の処理。
	/// 戻り値が非 null の場合はリプレイファイル生成を開始する。
	/// </summary>
	public async Task<EewState?> OnEewFinalReport(
		string eventId, DateTime? originTime, DateTime reportTime,
		double? magnitude, double? depthKm)
	{
		var acquired = await _state.TryAcquireLock("eew", eventId, TimeSpan.FromMinutes(10));
		if (!acquired)
		{
			_logger.LogDebug($"EEWのロック取得失敗（重複）: {eventId}");
			return null;
		}

		var state = new EewState
		{
			EventId = eventId,
			OriginTime = originTime,
			ReportTime = reportTime,
			Magnitude = magnitude,
			DepthKm = depthKm,
		};

		_logger.LogInformation($"EEWトリガー: {eventId} (M={magnitude}, depth={depthKm}km)");
		return state;
	}

	public async Task CompleteAsync(string eventId)
	{
		await _state.ReleaseLock("eew", eventId);
	}
}
