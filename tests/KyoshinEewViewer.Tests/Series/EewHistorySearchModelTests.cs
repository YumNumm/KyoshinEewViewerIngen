using KyoshinEewViewer.Series.EewHistory.Models;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Tests.Series;

public class EewHistorySearchModelTests
{
	[Fact(DisplayName = "入力内容から検索条件へ変換できる")]
	public void 検索条件への変換()
	{
		var model = new EewHistorySearchModel
		{
			OriginTimeFrom = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.FromHours(9)),
			OriginTimeTo = new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.FromHours(9)),
			MagnitudeFrom = 5.0,
			MagnitudeTo = 7.0,
			DepthFrom = 10,
			DepthTo = 100,
			IntensityFrom = EewHistorySearchModel.IntensityChoices.Single(c => c.Value == Generated.JmaIntensity._5Minus),
			IntensityTo = EewHistorySearchModel.IntensityChoices.Single(c => c.Value == Generated.JmaIntensity._6Plus),
			Status = EewHistorySearchModel.StatusChoices.Single(c => c.Value == Generated.TelegramStatus.TRAINING),
			WarningFilter = EewHistorySearchModel.WarningFilterChoices.Single(c => c.Value == true),
		};

		var condition = model.ToCondition();

		Assert.Equal(model.OriginTimeFrom, condition.OriginTimeFrom);
		Assert.Equal(model.OriginTimeTo, condition.OriginTimeTo);
		Assert.Equal(5.0, condition.MagnitudeFrom);
		Assert.Equal(7.0, condition.MagnitudeTo);
		Assert.Equal(10, condition.DepthFrom);
		Assert.Equal(100, condition.DepthTo);
		Assert.Equal(Generated.JmaIntensity._5Minus, condition.IntensityFrom);
		Assert.Equal(Generated.JmaIntensity._6Plus, condition.IntensityTo);
		Assert.Equal(Generated.TelegramStatus.TRAINING, condition.Status);
		Assert.True(condition.IsWarning);
	}

	[Fact(DisplayName = "Reset で入力が初期状態に戻る")]
	public void 入力のリセット()
	{
		var model = new EewHistorySearchModel
		{
			MagnitudeFrom = 5.0,
			WarningFilter = EewHistorySearchModel.WarningFilterChoices[1],
			Error = "失敗",
		};

		model.Reset();

		Assert.Null(model.MagnitudeFrom);
		Assert.Null(model.WarningFilter.Value);
		Assert.Null(model.Error);
		Assert.Null(model.Status.Value);
	}
}
