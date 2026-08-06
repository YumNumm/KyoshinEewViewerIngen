using KyoshinEewViewer.Core;
using KyoshinMonitorLib;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Services.EqMonitor;

/// <summary>
/// EQMonitor API の値を KEVi の型へ変換する
/// </summary>
public static class EqMonitorConverters
{
	/// <summary>
	/// 震度を変換する
	/// </summary>
	/// <remarks>
	/// API の <c>!5-</c> / <c>!6-</c> は「5弱以上」「6弱以上」と推定された震度速報向けの値。
	/// KEVi の <see cref="JmaIntensity"/> に「以上」を表す値はないため下限の震度として扱う
	/// </remarks>
	public static JmaIntensity ToJmaIntensity(this Generated.JmaIntensity intensity)
		=> intensity switch
		{
			Generated.JmaIntensity._0 => JmaIntensity.Int0,
			Generated.JmaIntensity._1 => JmaIntensity.Int1,
			Generated.JmaIntensity._2 => JmaIntensity.Int2,
			Generated.JmaIntensity._3 => JmaIntensity.Int3,
			Generated.JmaIntensity._4 => JmaIntensity.Int4,
			Generated.JmaIntensity.Over5Minus or Generated.JmaIntensity._5Minus => JmaIntensity.Int5Lower,
			Generated.JmaIntensity._5Plus => JmaIntensity.Int5Upper,
			Generated.JmaIntensity.Over6Minus or Generated.JmaIntensity._6Minus => JmaIntensity.Int6Lower,
			Generated.JmaIntensity._6Plus => JmaIntensity.Int6Upper,
			Generated.JmaIntensity._7 => JmaIntensity.Int7,
			_ => JmaIntensity.Unknown,
		};

	/// <inheritdoc cref="ToJmaIntensity(Generated.JmaIntensity)"/>
	public static JmaIntensity ToJmaIntensity(this Generated.JmaIntensity? intensity)
		=> intensity is { } value ? value.ToJmaIntensity() : JmaIntensity.Unknown;

	/// <summary>
	/// 震度が「以上」を伴う推定値か
	/// </summary>
	public static bool IsIntensityOver(this Generated.JmaIntensity intensity)
		=> intensity is Generated.JmaIntensity.Over5Minus or Generated.JmaIntensity.Over6Minus;

	/// <summary>
	/// 長周期地震動階級を変換する
	/// </summary>
	public static LpgmIntensity ToLpgmIntensity(this Generated.JmaLpgmIntensity intensity)
		=> intensity switch
		{
			Generated.JmaLpgmIntensity._0 => LpgmIntensity.LpgmInt0,
			Generated.JmaLpgmIntensity._1 => LpgmIntensity.LpgmInt1,
			Generated.JmaLpgmIntensity._2 => LpgmIntensity.LpgmInt2,
			Generated.JmaLpgmIntensity._3 => LpgmIntensity.LpgmInt3,
			Generated.JmaLpgmIntensity._4 => LpgmIntensity.LpgmInt4,
			_ => LpgmIntensity.Unknown,
		};

	/// <inheritdoc cref="ToLpgmIntensity(Generated.JmaLpgmIntensity)"/>
	public static LpgmIntensity ToLpgmIntensity(this Generated.JmaLpgmIntensity? intensity)
		=> intensity is { } value ? value.ToLpgmIntensity() : LpgmIntensity.Unknown;
}
