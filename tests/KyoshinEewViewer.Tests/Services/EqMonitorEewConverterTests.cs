using KyoshinEewViewer.Series.KyoshinMonitor.Models;
using KyoshinEewViewer.Series.KyoshinMonitor.Services.Eew;
using KyoshinMonitorLib;
using Newtonsoft.Json;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Tests.Services;

public class EqMonitorEewConverterTests
{
	private static readonly DateTime ReceiveTime = new(2025, 12, 12, 19, 15, 0);

	private static Generated.EewItemWithRelations CreateItem(
		Generated.EewHypocenter? hypocenter = null,
		Generated.EewIntensity? forecastIntensity = null,
		Generated.EewWarning? warning = null,
		Generated.EewAccuracy? accuracy = null,
		bool isCanceled = false,
		bool? isWarning = false)
		=> new()
		{
			Event_id = "20251212191438",
			Type = Generated.TelegramType.VXSE45,
			Status = Generated.TelegramStatus.NORMAL,
			Info_type = Generated.EewItemWithRelationsInfo_type.PUBLICATION,
			Serial_no = 3,
			Headline = null,
			Is_canceled = isCanceled,
			Is_warning = isWarning,
			Is_last_info = true,
			Origin_time = new DateTimeOffset(2025, 12, 12, 19, 14, 38, TimeSpan.FromHours(9)),
			Arrival_time = null,
			Hypocenter = hypocenter,
			Forecast_intensity = forecastIntensity,
			Accuracy = accuracy,
			Is_plum = false,
			Editorial_office = "仙台管区気象台",
			Report_time = new DateTimeOffset(2025, 12, 12, 19, 14, 50, TimeSpan.FromHours(9)),
			Warning = warning,
		};

	[Fact(DisplayName = "緊急地震速報の基本情報が変換される")]
	public void 基本情報の変換()
	{
		var item = CreateItem(
			new Generated.EewHypocenter
			{
				Code = "288",
				Name = "宮城県沖",
				Coordinates = new Generated.Coordinate { Latitude = 38.1, Longitude = 142.3 },
				Magnitude = 7.1,
				Depth = 60,
			},
			new Generated.EewIntensity
			{
				Max_intensity = new Generated.EewIntensityValue { Value = Generated.JmaIntensity._5Plus, Is_over = false },
				Regions = [],
			},
			isWarning: true);

		var eew = item.ToEew(ReceiveTime);

		Assert.Equal("20251212191438", eew.Id);
		Assert.Equal(EewSource.EqMonitor, eew.Source);
		Assert.Equal("EQMonitor(仙台管区気象台)", eew.DisplaySource);
		Assert.Equal(3, eew.SerialNo);
		Assert.True(eew.IsFinal);
		Assert.True(eew.IsWarning);
		Assert.Equal(JmaIntensity.Int5Upper, eew.MaxIntensity);
		Assert.False(eew.IsIntensityOver);

		Assert.NotNull(eew.Hypocenter);
		Assert.Equal("宮城県沖", eew.Hypocenter.Place);
		Assert.Equal(7.1f, eew.Hypocenter.Magnitude);
		Assert.Equal(60, eew.Hypocenter.Depth);
		Assert.Equal(38.1f, eew.Hypocenter.Location!.Latitude, 3);
	}

	[Fact(DisplayName = "深さが不明の場合は -1 になる")]
	public void 深さが不明の場合()
	{
		var item = CreateItem(new Generated.EewHypocenter { Code = "288", Name = "宮城県沖", Magnitude = null, Depth = null });

		var eew = item.ToEew(ReceiveTime);

		Assert.Equal(-1, eew.Hypocenter!.Depth);
		Assert.Null(eew.Hypocenter.Magnitude);
		Assert.Null(eew.Hypocenter.Location);
	}

	[Fact(DisplayName = "精度情報が変換され最終報レベルの精度が判別される")]
	public void 精度情報の変換()
	{
		var item = CreateItem(
			new Generated.EewHypocenter { Code = "288", Name = "宮城県沖", Magnitude = 7.1, Depth = 60 },
			accuracy: new Generated.EewAccuracy
			{
				Epicenter = 4,
				Hypocenter = 9,
				Depth = 5,
				Magnitude_calculation = 6,
				Number_of_magnitude_calculation = 10,
			});

		var accuracy = item.ToEew(ReceiveTime).Hypocenter!.Accuracy;

		Assert.NotNull(accuracy);
		Assert.True(accuracy.IsLocked);
		Assert.Equal(4, accuracy.LocationAccuracy);
		Assert.Equal(5, accuracy.DepthAccuracy);
		Assert.Equal(6, accuracy.MagnitudeAccuracy);
	}

	[Fact(DisplayName = "予想震度と警報地域がコードへ変換される")]
	public void 予想震度と警報地域の変換()
	{
		var item = CreateItem(
			forecastIntensity: new Generated.EewIntensity
			{
				Max_intensity = new Generated.EewIntensityValue { Value = Generated.JmaIntensity._6Minus, Is_over = true },
				Regions =
				[
					new Generated.EewIntensityItem
					{
						Code = "300",
						Name = "宮城県北部",
						Is_plum = false,
						Is_warning = true,
						Intensity = new Generated.EewIntensityValue { Value = Generated.JmaIntensity._6Minus, Is_over = false },
						Arrival_time = new Generated.EewIntensityRegionArrivalTimeTime { Type = Generated.EewIntensityRegionArrivalTimeType.ARRIVED },
					},
					// 震度不明の地域は塗り分けの対象にしない
					new Generated.EewIntensityItem
					{
						Code = "301",
						Name = "宮城県南部",
						Is_plum = false,
						Is_warning = false,
						Intensity = new Generated.EewIntensityValue { Value = (Generated.JmaIntensity)(-1), Is_over = false },
						Arrival_time = new Generated.EewIntensityRegionArrivalTimeTime { Type = Generated.EewIntensityRegionArrivalTimeType.ARRIVED },
					},
				],
			},
			warning: new Generated.EewWarning
			{
				Zones = [],
				Prefectures = [],
				Regions = [new Generated.EewWarningZoneItem { Code = "300", Name = "宮城県北部", Had_warning = false }],
			},
			isWarning: true);

		var eew = item.ToEew(ReceiveTime);

		Assert.True(eew.IsIntensityOver);
		Assert.NotNull(eew.IntensityForecastMap);
		Assert.Equal(JmaIntensity.Int6Lower, Assert.Contains(300, eew.IntensityForecastMap));
		Assert.DoesNotContain(301, eew.IntensityForecastMap);

		Assert.NotNull(eew.WarningAreas);
		Assert.Equal([300], eew.WarningAreas.Codes);
		Assert.True(eew.WarningAreas.IsWarningTelegram);
	}

	[Fact(DisplayName = "取消報は確実な取消として扱われる")]
	public void 取消報の変換()
	{
		var eew = CreateItem(isCanceled: true).ToEew(ReceiveTime);

		Assert.True(eew.IsCancelled);
		Assert.True(eew.IsTrueCancelled);
	}

	[Fact(DisplayName = "震度 5- のような C# 識別子にできない値がデシリアライズできる")]
	public void 震度のデシリアライズ()
	{
		// 生成されるクライアントの enum 名は正規化されるため、
		// JSON 側の値との対応が壊れていないことを確認する
		const string json = """
		{"items":[{"event_id":"20251212191438","status":"NORMAL","earthquake_type":"NORMAL",
		"origin_time":"2025-12-12T19:14:38+09:00","origin_time_precision":"SECOND",
		"datasources":["JMA_DISASTER_INFORMATION_XML"],
		"intensity":{"max_intensity":"5-","max_lpgm_intensity":"3"},
		"telegram_types":["VXSE53"]}]}
		""";

		var response = JsonConvert.DeserializeObject<Generated.EarthquakeListResponse>(json);

		var item = Assert.Single(response!.Items);
		Assert.Equal(Generated.JmaIntensity._5Minus, item.Intensity!.Max_intensity);
		Assert.Equal(Generated.JmaLpgmIntensity._3, item.Intensity.Max_lpgm_intensity);
		Assert.Equal(Generated.EarthquakeDatasource.JMA_DISASTER_INFORMATION_XML, Assert.Single(item.Datasources));
		Assert.Equal(Generated.EarthquakeTelegramType.VXSE53, Assert.Single(item.Telegram_types));
	}
}
