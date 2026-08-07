using KyoshinEewViewer.Series.KyoshinMonitor.Models;
using KyoshinEewViewer.Services.EqMonitor;
using KyoshinMonitorLib;
using System;
using System.Globalization;
using System.Linq;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Series.KyoshinMonitor.Services.Eew;

/// <summary>
/// EQMonitor API の緊急地震速報を KEVi の <see cref="Models.Eew"/> へ変換する
/// </summary>
public static class EqMonitorEewConverter
{
	public static Models.Eew ToEew(this Generated.EewItemWithRelations item, DateTime receiveTime)
	{
		var forecast = item.Forecast_intensity;
		// 警報地域は細分区域(regions)を使う。zones や prefectures はより粗い単位のため表示に適さない
		var warningRegions = item.Warning?.Regions ?? [];

		return new Models.Eew
		{
			Id = item.Event_id,
			Source = EewSource.EqMonitor,
			DisplaySource = string.IsNullOrEmpty(item.Editorial_office)
				? "EQMonitor"
				: $"EQMonitor({item.Editorial_office})",
			ReceiveTime = receiveTime,
			SerialNo = (int)item.Serial_no,
			IsFinal = item.Is_last_info,
			IsCancelled = item.Is_canceled,
			IsTrueCancelled = item.Is_canceled,

			MaxIntensity = forecast?.Max_intensity?.Value.ToJmaIntensity() ?? JmaIntensity.Unknown,
			IsIntensityOver = forecast?.Max_intensity?.Is_over ?? false,

			Hypocenter = item.Hypocenter is { } hypocenter ? new EewHypocenter
			{
				OccurrenceTime = (item.Origin_time ?? item.Arrival_time)?.LocalDateTime ?? receiveTime,
				Place = hypocenter.Name,
				Location = hypocenter.Coordinates is { } coordinates
					? new Location((float)coordinates.Latitude, (float)coordinates.Longitude)
					: null,
				Magnitude = hypocenter.Magnitude is { } magnitude ? (float)magnitude : null,
				// 深さは 0 が「ごく浅い」、700 が「700km以上」、null が「不明」
				Depth = hypocenter.Depth ?? -1,
				// 仮定震源要素かどうかは API から判別できない。
				// 該当する場合は震央の確からしさが 1 になるため、地図側はそちらで判断できる
				IsTemporary = false,
				Accuracy = item.Accuracy is { } accuracy ? new EewHypocenterAccuracy
				{
					IsLocked = (int)accuracy.Hypocenter == 9,
					LocationAccuracy = (int)accuracy.Epicenter,
					DepthAccuracy = (int)accuracy.Depth,
					MagnitudeAccuracy = (int)accuracy.Magnitude_calculation,
				} : null,
			} : null,

			IntensityForecastMap = forecast?.Regions
				.Select(r => (Code: ParseAreaCode(r.Code), Intensity: r.Intensity.Value.ToJmaIntensity()))
				.Where(r => r.Code != null && r.Intensity != JmaIntensity.Unknown)
				.ToDictionary(r => r.Code!.Value, r => r.Intensity),

			WarningAreas = warningRegions.Count > 0 ? new EewWarningAreas
			{
				DisplaySource = "EQMonitor",
				SerialNo = (int)item.Serial_no,
				Codes = warningRegions.Select(r => ParseAreaCode(r.Code)).Where(c => c != null).Select(c => c!.Value).ToArray(),
				Names = EewAreaGroups.Compressor.Compress(warningRegions.Select(r => r.Name).ToArray()),
				IsWarningTelegram = item.Type == Generated.TelegramType.VXSE45 && (item.Is_warning ?? false),
			} : null,

			IsWarning = item.Is_warning ?? false,
		};
	}

	/// <summary>
	/// 地域コードを数値へ変換する
	/// </summary>
	private static int? ParseAreaCode(string code)
		=> int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
}
