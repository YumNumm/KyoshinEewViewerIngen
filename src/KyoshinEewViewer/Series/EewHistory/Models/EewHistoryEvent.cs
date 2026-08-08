using KyoshinEewViewer.Series.KyoshinMonitor.Services.Eew;
using KyoshinMonitorLib;
using ReactiveUI;
using System;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Series.EewHistory.Models;

/// <summary>
/// EEWイベント履歴一覧の1件（最終報相当の要約）
/// </summary>
public class EewHistoryEvent : ReactiveObject
{
	/// <summary>
	/// API の最終報から一覧用の要約を組み立てる
	/// </summary>
	public static EewHistoryEvent FromFinalReport(Generated.EewItemWithRelations item)
	{
		var eew = item.ToEew(item.Report_time.LocalDateTime);
		return new EewHistoryEvent
		{
			EventId = item.Event_id,
			OriginTime = (item.Origin_time ?? item.Arrival_time ?? item.Report_time).LocalDateTime,
			Place = item.Hypocenter?.Name,
			Magnitude = item.Hypocenter?.Magnitude is { } magnitude ? (float)magnitude : null,
			Depth = item.Hypocenter?.Depth ?? -1,
			MaxIntensity = eew.MaxIntensity,
			IsIntensityOver = eew.IsIntensityOver,
			SerialNo = (int)item.Serial_no,
			IsWarning = item.Is_warning ?? false,
			IsCancelled = item.Is_canceled,
			IsFinal = item.Is_last_info,
			IsPlum = item.Is_plum,
			IsTraining = item.Status == Generated.TelegramStatus.TRAINING,
			IsTest = item.Status == Generated.TelegramStatus.TEST,
		};
	}

	/// <summary>
	/// イベントID（yyyyMMddHHmmss）
	/// </summary>
	public required string EventId { get; init; }

	/// <summary>
	/// 発生時刻（不明な場合は発表時刻など代替値）
	/// </summary>
	public DateTime OriginTime { get; init; }

	/// <summary>
	/// 震央地名
	/// </summary>
	public string? Place { get; init; }

	/// <summary>
	/// マグニチュード
	/// </summary>
	public float? Magnitude { get; init; }

	/// <summary>
	/// 震源の深さ(km)。0:ごく浅い、-1/null:不明
	/// </summary>
	public int? Depth { get; init; }

	/// <summary>
	/// 深さデータがないか
	/// </summary>
	public bool IsNoDepthData => Depth is null or < 0;

	/// <summary>
	/// ごく浅いか
	/// </summary>
	public bool IsVeryShallow => Depth == 0;

	/// <summary>
	/// 最大予測震度
	/// </summary>
	public JmaIntensity MaxIntensity { get; init; } = JmaIntensity.Unknown;

	/// <summary>
	/// 最大予測震度に「以上」が付くか
	/// </summary>
	public bool IsIntensityOver { get; init; }

	/// <summary>
	/// 最終報の報数
	/// </summary>
	public int SerialNo { get; init; }

	/// <summary>
	/// 警報を含むか
	/// </summary>
	public bool IsWarning { get; init; }

	/// <summary>
	/// 取消されているか
	/// </summary>
	public bool IsCancelled { get; init; }

	/// <summary>
	/// 最終報か
	/// </summary>
	public bool IsFinal { get; init; }

	/// <summary>
	/// PLUM法によるものか
	/// </summary>
	public bool IsPlum { get; init; }

	/// <summary>
	/// 訓練か
	/// </summary>
	public bool IsTraining { get; init; }

	/// <summary>
	/// 試験か
	/// </summary>
	public bool IsTest { get; init; }

	private bool _isSelecting;
	/// <summary>
	/// 一覧上で選択中か
	/// </summary>
	public bool IsSelecting
	{
		get => _isSelecting;
		set => this.RaiseAndSetIfChanged(ref _isSelecting, value);
	}
}
