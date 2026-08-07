using KyoshinEewViewer.Series.Earthquake.Models;
using KyoshinMonitorLib;
using System;
using System.Linq;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Services.EqMonitor;

/// <summary>
/// EQMonitor API の地震情報を KEVi の情報フラグメントへ変換する
/// </summary>
public static class EqMonitorEarthquakeConverter
{
	/// <summary>
	/// 震源が未確定の段階で震央地名の代わりに表示する文言<br/>
	/// 気象庁の震度速報における「震源・震源の規模は調査中」に倣う
	/// </summary>
	private const string UnknownPlace = "調査中";

	/// <summary>
	/// 地震情報一覧の 1 件を情報フラグメントへ変換する
	/// </summary>
	/// <returns>震源も震度も持たない場合は null</returns>
	public static EarthquakeInformationFragment? ToFragment(this Generated.EarthquakePartial item)
	{
		var isTraining = item.Status == Generated.TelegramStatus.TRAINING;
		var isTest = item.Status == Generated.TelegramStatus.TEST;

		// 電文の発表時刻は一覧からは得られないため、発生時刻もしくは検知時刻で代用する
		var arrivedTime = (item.Origin_time ?? item.Arrival_time)?.LocalDateTime ?? DateTime.MinValue;

		var hypocenter = item.Hypocenter;
		var maxIntensity = item.Intensity?.Max_intensity.ToJmaIntensity() ?? JmaIntensity.Unknown;
		var hasIntensity = item.Intensity != null;

		// 震源が未確定の場合は震度速報として扱う
		if (hypocenter?.Coordinates is not { } coordinates)
		{
			if (!hasIntensity)
				return null;

			return new IntensityInformationFragment
			{
				ArrivedTime = arrivedTime,
				BasedTelegram = null,
				Title = "震度速報",
				IsTraining = isTraining,
				IsTest = isTest,

				Place = hypocenter?.Name ?? UnknownPlace,
				DetectionTime = (item.Arrival_time ?? item.Origin_time)?.LocalDateTime ?? arrivedTime,
				MaxIntensity = maxIntensity,
			};
		}

		var (magnitude, magnitudeAlternativeText) = ConvertMagnitude(hypocenter.Magnitude);
		var location = new Location((float)coordinates.Latitude, (float)coordinates.Longitude);
		var occurrenceTime = (item.Origin_time ?? item.Arrival_time)?.LocalDateTime ?? arrivedTime;

		// 震度を伴わない場合は震源に関する情報として扱う
		if (!hasIntensity)
			return new HypocenterInformationFragment
			{
				ArrivedTime = arrivedTime,
				BasedTelegram = null,
				Title = "震源に関する情報",
				IsTraining = isTraining,
				IsTest = isTest,

				OccurrenceTime = occurrenceTime,
				Place = hypocenter.Name ?? UnknownPlace,
				Location = location,
				LocationError = null,
				Magnitude = magnitude,
				MagnitudeAlternativeText = magnitudeAlternativeText,
				Depth = ConvertDepth(hypocenter.Depth),
				DepthError = null,
			};

		// 長周期地震動階級を伴う場合はその情報として扱う
		if (item.Intensity?.Max_lpgm_intensity is { } lpgm)
			return new LpgmIntensityInformationFragment
			{
				ArrivedTime = arrivedTime,
				BasedTelegram = null,
				Title = "長周期地震動に関する観測情報",
				IsTraining = isTraining,
				IsTest = isTest,

				OccurrenceTime = occurrenceTime,
				Place = hypocenter.Name ?? UnknownPlace,
				Location = location,
				LocationError = null,
				Magnitude = magnitude,
				MagnitudeAlternativeText = magnitudeAlternativeText,
				Depth = ConvertDepth(hypocenter.Depth),
				DepthError = null,

				MaxIntensity = maxIntensity,
				IsForeign = item.Earthquake_type == Generated.EarthquakeType.DISTANT,
				IsVolcano = item.Earthquake_type == Generated.EarthquakeType.VOLCANO,

				MaxLpgmIntensity = lpgm.ToLpgmIntensity(),
			};

		return new HypocenterAndIntensityInformationFragment
		{
			ArrivedTime = arrivedTime,
			BasedTelegram = null,
			Title = "震源・震度に関する情報",
			IsTraining = isTraining,
			IsTest = isTest,

			OccurrenceTime = occurrenceTime,
			Place = hypocenter.Name ?? UnknownPlace,
			Location = location,
			LocationError = null,
			Magnitude = magnitude,
			MagnitudeAlternativeText = magnitudeAlternativeText,
			Depth = ConvertDepth(hypocenter.Depth),
			DepthError = null,

			MaxIntensity = maxIntensity,
			IsForeign = item.Earthquake_type == Generated.EarthquakeType.DISTANT,
			IsVolcano = item.Earthquake_type == Generated.EarthquakeType.VOLCANO,
		};
	}

	/// <summary>
	/// マグニチュードを数値と代替テキストへ変換する
	/// </summary>
	private static (float Magnitude, string? AlternativeText) ConvertMagnitude(Generated.Magnitude magnitude)
		=> magnitude.Type switch
		{
			Generated.MagnitudeType.NORMAL when magnitude.Value is { } value => ((float)value, null),
			Generated.MagnitudeType.OVER_M8 => (float.NaN, "M8を超える巨大地震"),
			_ => (float.NaN, "M不明"),
		};

	/// <summary>
	/// 深さを km へ変換する
	/// </summary>
	/// <remarks>0 は「ごく浅い」、-1 は「不明」として扱われる</remarks>
	private static int ConvertDepth(Generated.Depth depth)
		=> depth.Type switch
		{
			Generated.DepthType.NORMAL when depth.Value is { } value => (int)value,
			Generated.DepthType.SHALLOW => 0,
			Generated.DepthType.OVER_700 => 700,
			_ => -1,
		};
}
