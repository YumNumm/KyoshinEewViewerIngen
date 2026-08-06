using KyoshinEewViewer.Core;
using KyoshinEewViewer.Series.Earthquake.Models;
using KyoshinEewViewer.Services.EqMonitor;
using KyoshinMonitorLib;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Tests.Services;

public class EqMonitorEarthquakeConverterTests
{
	private static Generated.EarthquakePartial CreateItem(
		Generated.Hypocenter? hypocenter = null,
		Generated.IntensityPartial? intensity = null,
		Generated.TelegramStatus status = Generated.TelegramStatus.NORMAL,
		Generated.EarthquakeType type = Generated.EarthquakeType.NORMAL)
		=> new()
		{
			Event_id = "20251212191438",
			Status = status,
			Earthquake_type = type,
			Origin_time = new DateTimeOffset(2025, 12, 12, 19, 14, 38, TimeSpan.FromHours(9)),
			Arrival_time = new DateTimeOffset(2025, 12, 12, 19, 15, 0, TimeSpan.FromHours(9)),
			Origin_time_precision = Generated.OriginTimePrecision.SECOND,
			Hypocenter = hypocenter,
			Intensity = intensity,
			Datasources = [],
			Telegram_types = [],
		};

	private static Generated.Hypocenter CreateHypocenter(
		Generated.MagnitudeType magnitudeType = Generated.MagnitudeType.NORMAL,
		double? magnitudeValue = 6.5,
		Generated.DepthType depthType = Generated.DepthType.NORMAL,
		double? depthValue = 60)
		=> new()
		{
			Name = "宮城県沖",
			Coordinates = new Generated.Coordinate { Latitude = 38.1, Longitude = 142.3 },
			Magnitude = new Generated.Magnitude { Type = magnitudeType, Value = magnitudeValue },
			Depth = new Generated.Depth { Type = depthType, Value = depthValue },
		};

	[Fact(DisplayName = "震源と震度が揃っている場合は震源・震度に関する情報へ変換される")]
	public void 震源と震度が揃っている場合()
	{
		var item = CreateItem(
			CreateHypocenter(),
			new Generated.IntensityPartial { Max_intensity = Generated.JmaIntensity._5Minus });

		var fragment = Assert.IsType<HypocenterAndIntensityInformationFragment>(item.ToFragment());

		Assert.Equal("震源・震度に関する情報", fragment.Title);
		Assert.Equal("宮城県沖", fragment.Place);
		Assert.Equal(JmaIntensity.Int5Lower, fragment.MaxIntensity);
		Assert.Equal(6.5f, fragment.Magnitude);
		Assert.Null(fragment.MagnitudeAlternativeText);
		Assert.Equal(60, fragment.Depth);
		Assert.Equal(38.1f, fragment.Location.Latitude, 3);
		Assert.Equal(142.3f, fragment.Location.Longitude, 3);
		// 電文本文を伴わないため観測点ごとの震度は読み出せない
		Assert.Null(fragment.BasedTelegram);
	}

	[Fact(DisplayName = "震源が未確定の場合は震度速報へ変換される")]
	public void 震源が未確定の場合()
	{
		var item = CreateItem(
			intensity: new Generated.IntensityPartial { Max_intensity = Generated.JmaIntensity.Over5Minus });

		var fragment = Assert.IsType<IntensityInformationFragment>(item.ToFragment());

		Assert.Equal("震度速報", fragment.Title);
		Assert.Equal("調査中", fragment.Place);
		// 「5弱以上と推定」は下限の震度として扱う
		Assert.Equal(JmaIntensity.Int5Lower, fragment.MaxIntensity);
	}

	[Fact(DisplayName = "震度を伴わない場合は震源に関する情報へ変換される")]
	public void 震度を伴わない場合()
	{
		var fragment = Assert.IsType<HypocenterInformationFragment>(CreateItem(CreateHypocenter()).ToFragment());

		Assert.Equal("震源に関する情報", fragment.Title);
		Assert.Equal("宮城県沖", fragment.Place);
	}

	[Fact(DisplayName = "長周期地震動階級を伴う場合はその情報へ変換される")]
	public void 長周期地震動階級を伴う場合()
	{
		var item = CreateItem(
			CreateHypocenter(),
			new Generated.IntensityPartial
			{
				Max_intensity = Generated.JmaIntensity._6Plus,
				Max_lpgm_intensity = Generated.JmaLpgmIntensity._3,
			});

		var fragment = Assert.IsType<LpgmIntensityInformationFragment>(item.ToFragment());

		Assert.Equal(JmaIntensity.Int6Upper, fragment.MaxIntensity);
		Assert.Equal(LpgmIntensity.LpgmInt3, fragment.MaxLpgmIntensity);
	}

	[Fact(DisplayName = "震源も震度も持たない場合は変換されない")]
	public void 震源も震度も持たない場合()
		=> Assert.Null(CreateItem().ToFragment());

	[Theory(DisplayName = "マグニチュードが数値で得られない場合は代替テキストが設定される")]
	[InlineData(Generated.MagnitudeType.UNKNOWN, null, "M不明")]
	[InlineData(Generated.MagnitudeType.OVER_M8, null, "M8を超える巨大地震")]
	[InlineData(Generated.MagnitudeType.NORMAL, null, "M不明")]
	public void マグニチュードの代替テキスト(Generated.MagnitudeType type, double? value, string expected)
	{
		var item = CreateItem(CreateHypocenter(type, value));

		var fragment = Assert.IsType<HypocenterInformationFragment>(item.ToFragment());

		Assert.True(float.IsNaN(fragment.Magnitude));
		Assert.Equal(expected, fragment.MagnitudeAlternativeText);
	}

	[Theory(DisplayName = "深さが KEVi の表現へ変換される")]
	[InlineData(Generated.DepthType.NORMAL, 120d, 120)]
	[InlineData(Generated.DepthType.SHALLOW, null, 0)]
	[InlineData(Generated.DepthType.OVER_700, null, 700)]
	[InlineData(Generated.DepthType.UNKNOWN, null, -1)]
	public void 深さの変換(Generated.DepthType type, double? value, int expected)
	{
		var item = CreateItem(CreateHypocenter(depthType: type, depthValue: value));

		var fragment = Assert.IsType<HypocenterInformationFragment>(item.ToFragment());

		Assert.Equal(expected, fragment.Depth);
	}

	[Theory(DisplayName = "電文の状態が訓練･試験として引き継がれる")]
	[InlineData(Generated.TelegramStatus.NORMAL, false, false)]
	[InlineData(Generated.TelegramStatus.TRAINING, true, false)]
	[InlineData(Generated.TelegramStatus.TEST, false, true)]
	public void 訓練と試験の引き継ぎ(Generated.TelegramStatus status, bool isTraining, bool isTest)
	{
		var fragment = CreateItem(CreateHypocenter(), status: status).ToFragment();

		Assert.NotNull(fragment);
		Assert.Equal(isTraining, fragment.IsTraining);
		Assert.Equal(isTest, fragment.IsTest);
	}

	[Theory(DisplayName = "地震の種別が遠地地震･大規模噴火として引き継がれる")]
	[InlineData(Generated.EarthquakeType.NORMAL, false, false)]
	[InlineData(Generated.EarthquakeType.DISTANT, true, false)]
	[InlineData(Generated.EarthquakeType.VOLCANO, false, true)]
	public void 地震種別の引き継ぎ(Generated.EarthquakeType type, bool isForeign, bool isVolcano)
	{
		var item = CreateItem(
			CreateHypocenter(),
			new Generated.IntensityPartial { Max_intensity = Generated.JmaIntensity._4 },
			type: type);

		var fragment = Assert.IsType<HypocenterAndIntensityInformationFragment>(item.ToFragment());

		Assert.Equal(isForeign, fragment.IsForeign);
		Assert.Equal(isVolcano, fragment.IsVolcano);
	}

	[Theory(DisplayName = "震度が KEVi の震度へ変換される")]
	[InlineData(Generated.JmaIntensity._0, JmaIntensity.Int0)]
	[InlineData(Generated.JmaIntensity._4, JmaIntensity.Int4)]
	[InlineData(Generated.JmaIntensity.Over5Minus, JmaIntensity.Int5Lower)]
	[InlineData(Generated.JmaIntensity._5Minus, JmaIntensity.Int5Lower)]
	[InlineData(Generated.JmaIntensity._5Plus, JmaIntensity.Int5Upper)]
	[InlineData(Generated.JmaIntensity.Over6Minus, JmaIntensity.Int6Lower)]
	[InlineData(Generated.JmaIntensity._6Minus, JmaIntensity.Int6Lower)]
	[InlineData(Generated.JmaIntensity._6Plus, JmaIntensity.Int6Upper)]
	[InlineData(Generated.JmaIntensity._7, JmaIntensity.Int7)]
	public void 震度の変換(Generated.JmaIntensity source, JmaIntensity expected)
		=> Assert.Equal(expected, source.ToJmaIntensity());
}
