using System;
using System.Collections.Generic;
using System.Linq;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Series.EewHistory.Models;

/// <summary>
/// EEW履歴からのリプレイ開始要求
/// </summary>
/// <remarks>
/// 既存リプレイホストへの具体接続は購読側が実装する。
/// Series はこの要求をイベントとして通知するだけに留める。
/// </remarks>
public sealed class EewHistoryReplayRequest(
	string eventId,
	IReadOnlyList<EewHistoryListItem> reports,
	EewHistoryListItem sourceEvent)
{
	/// <summary>リプレイ対象のイベントID</summary>
	public string EventId { get; } = eventId;

	/// <summary>対象イベントに属する各報（時系列）</summary>
	public IReadOnlyList<EewHistoryListItem> Reports { get; } = reports;

	/// <summary>一覧上で選択されていたイベント（最終報）</summary>
	public EewHistoryListItem SourceEvent { get; } = sourceEvent;

	/// <summary>API 生データ（リプレイホスト接続用）</summary>
	public IReadOnlyList<Generated.EewItemWithRelations> RawItems { get; }
		= reports.Select(r => r.Item).ToArray();

	/// <summary>要求時刻</summary>
	public DateTime RequestedAt { get; } = DateTime.Now;
}
