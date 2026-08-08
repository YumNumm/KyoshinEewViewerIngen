using KyoshinEewViewer.Services.EqMonitor;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Tests.Services;

public class EqMonitorEewSearchConditionTests
{
	[Fact(DisplayName = "範囲の上下が逆でも入れ替えて正規化される")]
	public void 範囲の正規化()
	{
		var from = new DateTimeOffset(2025, 1, 10, 0, 0, 0, TimeSpan.FromHours(9));
		var to = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.FromHours(9));

		var normalized = new EqMonitorEewSearchCondition
		{
			OriginTimeFrom = from,
			OriginTimeTo = to,
			MagnitudeFrom = 7.0,
			MagnitudeTo = 5.0,
			DepthFrom = 100,
			DepthTo = 10,
			IntensityFrom = Generated.JmaIntensity._6Minus,
			IntensityTo = Generated.JmaIntensity._3,
			IsWarning = true,
			Status = Generated.TelegramStatus.TRAINING,
		}.Normalize();

		Assert.Equal(to, normalized.OriginTimeFrom);
		Assert.Equal(from, normalized.OriginTimeTo);
		Assert.Equal(5.0, normalized.MagnitudeFrom);
		Assert.Equal(7.0, normalized.MagnitudeTo);
		Assert.Equal(10, normalized.DepthFrom);
		Assert.Equal(100, normalized.DepthTo);
		Assert.Equal(Generated.JmaIntensity._3, normalized.IntensityFrom);
		Assert.Equal(Generated.JmaIntensity._6Minus, normalized.IntensityTo);
		Assert.True(normalized.IsWarning);
		Assert.Equal(Generated.TelegramStatus.TRAINING, normalized.Status);
	}

	[Fact(DisplayName = "API へ渡すクエリ値が期待どおりに組み立てられる")]
	public void クエリ値の変換()
	{
		var condition = new EqMonitorEewSearchCondition
		{
			MagnitudeFrom = 5.5,
			DepthTo = 100,
			Status = Generated.TelegramStatus.TEST,
			IsWarning = true,
		};

		Assert.Equal("5.5", EqMonitorEewSearchCondition.ToQueryValue(condition.MagnitudeFrom));
		Assert.Equal("100", EqMonitorEewSearchCondition.ToQueryValue(condition.DepthTo));
		Assert.Equal([Generated.TelegramStatus.TEST], condition.ToStatuses());
		Assert.Equal("true", condition.ToIsWarningQueryValue());
		Assert.Null(new EqMonitorEewSearchCondition().ToStatuses());
		Assert.Null(new EqMonitorEewSearchCondition().ToIsWarningQueryValue());
		Assert.Equal("false", new EqMonitorEewSearchCondition { IsWarning = false }.ToIsWarningQueryValue());
	}

	[Fact(DisplayName = "条件未指定のとき IsEmpty になる")]
	public void 空条件の判定()
	{
		Assert.True(new EqMonitorEewSearchCondition().IsEmpty);
		Assert.False(new EqMonitorEewSearchCondition { IsWarning = true }.IsEmpty);
	}
}
