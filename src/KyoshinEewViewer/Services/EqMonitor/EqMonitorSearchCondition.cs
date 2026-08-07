using System;
using System.Collections.Generic;
using System.Globalization;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Services.EqMonitor;

/// <summary>
/// 地震履歴の検索条件
/// </summary>
/// <remarks>
/// GET /v2/earthquake のクエリパラメータに対応する
/// </remarks>
public record EqMonitorSearchCondition
{
	/// <summary>発生日時の下限</summary>
	public DateTimeOffset? OriginTimeFrom { get; init; }
	/// <summary>発生日時の上限</summary>
	public DateTimeOffset? OriginTimeTo { get; init; }

	/// <summary>マグニチュードの下限</summary>
	public double? MagnitudeFrom { get; init; }
	/// <summary>マグニチュードの上限</summary>
	public double? MagnitudeTo { get; init; }

	/// <summary>深さ(km)の下限</summary>
	public double? DepthFrom { get; init; }
	/// <summary>深さ(km)の上限</summary>
	public double? DepthTo { get; init; }

	/// <summary>最大震度の下限</summary>
	public Generated.JmaIntensity? IntensityFrom { get; init; }
	/// <summary>最大震度の上限</summary>
	public Generated.JmaIntensity? IntensityTo { get; init; }

	/// <summary>最大長周期地震動階級の下限</summary>
	public Generated.JmaLpgmIntensity? LpgmIntensityFrom { get; init; }
	/// <summary>最大長周期地震動階級の上限</summary>
	public Generated.JmaLpgmIntensity? LpgmIntensityTo { get; init; }

	/// <summary>震央の緯度の下限</summary>
	public double? LatitudeFrom { get; init; }
	/// <summary>震央の緯度の上限</summary>
	public double? LatitudeTo { get; init; }
	/// <summary>震央の経度の下限</summary>
	public double? LongitudeFrom { get; init; }
	/// <summary>震央の経度の上限</summary>
	public double? LongitudeTo { get; init; }

	/// <summary>地震の種別</summary>
	public Generated.EarthquakeType? EarthquakeType { get; init; }
	/// <summary>電文の状態</summary>
	public Generated.TelegramStatus? Status { get; init; }
	/// <summary>地震データのソース</summary>
	public Generated.EarthquakeDatasource? Datasource { get; init; }
	/// <summary>電文種別</summary>
	public Generated.EarthquakeTelegramType? TelegramType { get; init; }

	/// <summary>並び替えの項目</summary>
	public Generated.EarthquakeSortBy SortBy { get; init; } = Generated.EarthquakeSortBy.Event_id;
	/// <summary>並び順</summary>
	public Generated.SortOrder SortOrder { get; init; } = Generated.SortOrder.DESC;

	/// <summary>
	/// カーソルによるページングを利用できるか
	/// </summary>
	/// <remarks>
	/// カーソルは event_id を基準に絞り込むため、
	/// 他の項目で並べ替えると結果に重複と欠落が生じる
	/// </remarks>
	public bool CanUseCursor => SortBy == Generated.EarthquakeSortBy.Event_id;

	/// <summary>
	/// 何も絞り込んでいないか
	/// </summary>
	public bool IsEmpty =>
		OriginTimeFrom == null && OriginTimeTo == null &&
		MagnitudeFrom == null && MagnitudeTo == null &&
		DepthFrom == null && DepthTo == null &&
		IntensityFrom == null && IntensityTo == null &&
		LpgmIntensityFrom == null && LpgmIntensityTo == null &&
		LatitudeFrom == null && LatitudeTo == null &&
		LongitudeFrom == null && LongitudeTo == null &&
		EarthquakeType == null && Status == null &&
		Datasource == null && TelegramType == null;

	/// <summary>
	/// 上下が入れ替わっている範囲を直した条件を返す
	/// </summary>
	/// <remarks>入力の誤りをエラーにせず、そのまま検索できるようにする</remarks>
	public EqMonitorSearchCondition Normalize()
	{
		var (originFrom, originTo) = Order(OriginTimeFrom, OriginTimeTo);
		var (magnitudeFrom, magnitudeTo) = Order(MagnitudeFrom, MagnitudeTo);
		var (depthFrom, depthTo) = Order(DepthFrom, DepthTo);
		var (intensityFrom, intensityTo) = Order(IntensityFrom, IntensityTo);
		var (lpgmFrom, lpgmTo) = Order(LpgmIntensityFrom, LpgmIntensityTo);
		var (latitudeFrom, latitudeTo) = Order(LatitudeFrom, LatitudeTo);
		var (longitudeFrom, longitudeTo) = Order(LongitudeFrom, LongitudeTo);

		return this with
		{
			OriginTimeFrom = originFrom,
			OriginTimeTo = originTo,
			MagnitudeFrom = magnitudeFrom,
			MagnitudeTo = magnitudeTo,
			DepthFrom = depthFrom,
			DepthTo = depthTo,
			IntensityFrom = intensityFrom,
			IntensityTo = intensityTo,
			LpgmIntensityFrom = lpgmFrom,
			LpgmIntensityTo = lpgmTo,
			LatitudeFrom = latitudeFrom,
			LatitudeTo = latitudeTo,
			LongitudeFrom = longitudeFrom,
			LongitudeTo = longitudeTo,
		};
	}

	private static (T? From, T? To) Order<T>(T? from, T? to) where T : struct
		=> from is { } f && to is { } t && Comparer<T>.Default.Compare(f, t) > 0 ? (to, from) : (from, to);

	/// <summary>
	/// 電文の状態の指定を API へ渡す形へ変換する
	/// </summary>
	/// <remarks>未指定のときは API 既定の NORMAL のみが対象になる</remarks>
	public IEnumerable<Generated.TelegramStatus>? ToStatuses()
		=> Status is { } status ? [status] : null;

	/// <summary>
	/// 電文種別の指定を API へ渡す形へ変換する
	/// </summary>
	public IEnumerable<Generated.EarthquakeTelegramType>? ToTelegramTypes()
		=> TelegramType is { } type ? [type] : null;

	/// <summary>
	/// 実数のクエリ値へ変換する
	/// </summary>
	public static string? ToQueryValue(double? value)
		=> value?.ToString(CultureInfo.InvariantCulture);
}
