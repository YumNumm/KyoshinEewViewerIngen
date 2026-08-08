using KyoshinEewViewer.Series.EewHistory.Models;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Tests.Series;

public class EewHistoryReportOrderingTests
{
	private static Generated.EewItemWithRelations CreateItem(
		string eventId,
		double serialNo,
		DateTimeOffset reportTime)
		=> new()
		{
			Event_id = eventId,
			Type = Generated.TelegramType.VXSE45,
			Status = Generated.TelegramStatus.NORMAL,
			Info_type = Generated.EewItemWithRelationsInfo_type.PUBLICATION,
			Serial_no = serialNo,
			Headline = null,
			Is_canceled = false,
			Is_warning = false,
			Is_last_info = false,
			Origin_time = reportTime,
			Arrival_time = null,
			Hypocenter = null,
			Forecast_intensity = null,
			Accuracy = null,
			Is_plum = false,
			Editorial_office = null,
			Report_time = reportTime,
			Warning = null,
		};

	[Fact(DisplayName = "全報は serial_no 昇順、同値なら report_time 昇順で並ぶ")]
	public void 報の並べ替え()
	{
		var items = new[]
		{
			CreateItem("20251212191438", 3, new DateTimeOffset(2025, 12, 12, 19, 15, 0, TimeSpan.FromHours(9))),
			CreateItem("20251212191438", 1, new DateTimeOffset(2025, 12, 12, 19, 14, 40, TimeSpan.FromHours(9))),
			CreateItem("20251212191438", 2, new DateTimeOffset(2025, 12, 12, 19, 14, 55, TimeSpan.FromHours(9))),
			CreateItem("20251212191438", 2, new DateTimeOffset(2025, 12, 12, 19, 14, 50, TimeSpan.FromHours(9))),
		};

		var ordered = EewHistoryReportOrdering.OrderBySerialThenReportTime(items);

		Assert.Equal([1d, 2d, 2d, 3d], ordered.Select(i => i.Serial_no));
		Assert.Equal(
			new DateTimeOffset(2025, 12, 12, 19, 14, 50, TimeSpan.FromHours(9)),
			ordered[1].Report_time);
		Assert.Equal(
			new DateTimeOffset(2025, 12, 12, 19, 14, 55, TimeSpan.FromHours(9)),
			ordered[2].Report_time);
	}

	[Fact(DisplayName = "並べ替え後の一覧行は元の最終報を保持する")]
	public void 一覧行への変換()
	{
		var items = new[]
		{
			CreateItem("20251212191438", 2, new DateTimeOffset(2025, 12, 12, 19, 14, 50, TimeSpan.FromHours(9))),
			CreateItem("20251212191438", 1, new DateTimeOffset(2025, 12, 12, 19, 14, 40, TimeSpan.FromHours(9))),
		};

		var listItems = EewHistoryReportOrdering.ToOrderedListItems(items);

		Assert.Equal([1, 2], listItems.Select(i => i.SerialNo));
		Assert.Equal("20251212191438", listItems[0].EventId);
		Assert.Same(items[1], listItems[0].Item);
		Assert.NotNull(listItems[0].Eew);
	}
}
