using System;

namespace ReplayGenerator.Domain;

/// <summary>
/// EEWトリガーで生成するリプレイの時間窓。
/// M5.0以上または深さ150km以上の場合は前後3分、それ以外は前後1分。
/// </summary>
public static class EewReplayWindow
{
	private const int NormalSeconds = 60;
	private const int ExtendedSeconds = 180;

	public static (int PreSeconds, int PostSeconds) ComputeMargins(double? magnitude, double? depthKm)
	{
		if (magnitude >= 5.0 || depthKm >= 150)
			return (ExtendedSeconds, ExtendedSeconds);
		return (NormalSeconds, NormalSeconds);
	}
}
