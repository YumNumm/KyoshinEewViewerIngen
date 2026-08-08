using System.Collections.Generic;
using System.Linq;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Series.EewHistory.Models;

/// <summary>
/// 同一イベントの緊急地震速報を並べ替える
/// </summary>
public static class EewHistoryReportOrdering
{
	/// <summary>
	/// serial_no 昇順、同値のときは report_time 昇順で並べる
	/// </summary>
	public static IReadOnlyList<Generated.EewItemWithRelations> OrderBySerialThenReportTime(
		IEnumerable<Generated.EewItemWithRelations> items)
		=> items
			.OrderBy(i => i.Serial_no)
			.ThenBy(i => i.Report_time)
			.ToList();

	/// <summary>
	/// serial_no / report_time 順に並べた一覧行を返す
	/// </summary>
	public static IReadOnlyList<EewHistoryListItem> ToOrderedListItems(
		IEnumerable<Generated.EewItemWithRelations> items)
		=> OrderBySerialThenReportTime(items)
			.Select(i => new EewHistoryListItem(i))
			.ToList();
}
