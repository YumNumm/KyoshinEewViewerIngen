using KyoshinEewViewer.Series.KyoshinMonitor.Models;
using KyoshinEewViewer.Series.KyoshinMonitor.Services.Eew;
using KyoshinMonitorLib;
using ReactiveUI;
using System;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Series.EewHistory.Models;

/// <summary>
/// EEW履歴の一覧行<br/>
/// API の最終報 <see cref="Generated.EewItemWithRelations"/> を保持する
/// </summary>
public class EewHistoryListItem : ReactiveObject
{
	public EewHistoryListItem(Generated.EewItemWithRelations item)
	{
		Item = item;
		Eew = item.ToEew(item.Report_time.LocalDateTime);
		Summary = EewHistoryEvent.FromFinalReport(item);
	}

	/// <summary>
	/// API から取得した最終報
	/// </summary>
	public Generated.EewItemWithRelations Item { get; }

	/// <summary>
	/// 表示用に変換した緊急地震速報
	/// </summary>
	public Eew Eew { get; }

	/// <summary>
	/// 詳細パネル・リプレイへ渡す表示用EEW
	/// </summary>
	public Eew DisplayEew => Eew;

	/// <summary>
	/// 一覧表示向けの要約
	/// </summary>
	public EewHistoryEvent Summary { get; }

	/// <summary>イベントID</summary>
	public string EventId => Item.Event_id;

	/// <summary>報数</summary>
	public int SerialNo => (int)Item.Serial_no;

	/// <summary>発表時刻</summary>
	public DateTime ReportTime => Item.Report_time.LocalDateTime;

	/// <summary>発生時刻</summary>
	public DateTimeOffset? OriginTime => Item.Origin_time;

	/// <summary>
	/// 一覧表示用の時刻（発生時刻がなければ発表時刻）
	/// </summary>
	public DateTimeOffset DisplayTime => OriginTime ?? ReportTime;

	/// <summary>震源地名</summary>
	public string? Place => Item.Hypocenter?.Name;

	/// <summary>マグニチュード</summary>
	public double? Magnitude => Item.Hypocenter?.Magnitude;

	/// <summary>深さ(km)。不明のときは -1</summary>
	public int Depth => Item.Hypocenter?.Depth ?? -1;

	/// <summary>深さデータがないか</summary>
	public bool IsNoDepthData => Depth < 0;

	/// <summary>ごく浅いか</summary>
	public bool IsVeryShallow => Depth == 0;

	/// <summary>予想最大震度</summary>
	public JmaIntensity MaxIntensity => Eew.MaxIntensity;

	/// <summary>予想最大震度に「以上」が付くか</summary>
	public bool IsIntensityOver => Eew.IsIntensityOver;

	/// <summary>警報か</summary>
	public bool IsWarning => Item.Is_warning ?? false;

	/// <summary>取消報か</summary>
	public bool IsCancelled => Item.Is_canceled;

	/// <summary>最終報か</summary>
	public bool IsFinal => Item.Is_last_info;

	/// <summary>PLUM法か</summary>
	public bool IsPlum => Item.Is_plum;

	/// <summary>訓練か</summary>
	public bool IsTraining => Item.Status == Generated.TelegramStatus.TRAINING;

	/// <summary>試験か</summary>
	public bool IsTest => Item.Status == Generated.TelegramStatus.TEST;

	/// <summary>電文の見出し</summary>
	public string? Headline => Item.Headline;

	private bool _isSelecting;
	/// <summary>
	/// 該当項目が選択中か
	/// </summary>
	public bool IsSelecting
	{
		get => _isSelecting;
		set
		{
			this.RaiseAndSetIfChanged(ref _isSelecting, value);
			Summary.IsSelecting = value;
		}
	}
}
