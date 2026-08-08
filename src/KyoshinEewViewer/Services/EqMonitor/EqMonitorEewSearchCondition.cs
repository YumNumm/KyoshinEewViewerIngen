using System;
using System.Collections.Generic;
using System.Globalization;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Services.EqMonitor;

/// <summary>
/// 緊急地震速報履歴の検索条件
/// </summary>
/// <remarks>
/// GET /v2/eew のクエリパラメータに対応する
/// </remarks>
public record EqMonitorEewSearchCondition
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

	/// <summary>予想最大震度の下限</summary>
	public Generated.JmaIntensity? IntensityFrom { get; init; }
	/// <summary>予想最大震度の上限</summary>
	public Generated.JmaIntensity? IntensityTo { get; init; }

	/// <summary>電文の状態</summary>
	public Generated.TelegramStatus? Status { get; init; }

	/// <summary>
	/// 警報のみに絞り込むか<br/>
	/// <see langword="null"/> は絞り込まない。<see langword="false"/> は警報以外に絞り込む
	/// </summary>
	public bool? IsWarning { get; init; }

	/// <summary>
	/// 何も絞り込んでいないか
	/// </summary>
	public bool IsEmpty =>
		OriginTimeFrom == null && OriginTimeTo == null &&
		MagnitudeFrom == null && MagnitudeTo == null &&
		DepthFrom == null && DepthTo == null &&
		IntensityFrom == null && IntensityTo == null &&
		Status == null && IsWarning == null;

	/// <summary>
	/// 上下が入れ替わっている範囲を直した条件を返す
	/// </summary>
	/// <remarks>入力の誤りをエラーにせず、そのまま検索できるようにする</remarks>
	public EqMonitorEewSearchCondition Normalize()
	{
		var (originFrom, originTo) = Order(OriginTimeFrom, OriginTimeTo);
		var (magnitudeFrom, magnitudeTo) = Order(MagnitudeFrom, MagnitudeTo);
		var (depthFrom, depthTo) = Order(DepthFrom, DepthTo);
		var (intensityFrom, intensityTo) = Order(IntensityFrom, IntensityTo);

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
	/// 警報の指定を API へ渡す文字列へ変換する
	/// </summary>
	public string? ToIsWarningQueryValue()
		=> IsWarning?.ToString().ToLowerInvariant();

	/// <summary>
	/// 実数のクエリ値へ変換する
	/// </summary>
	public static string? ToQueryValue(double? value)
		=> value?.ToString(CultureInfo.InvariantCulture);
}
