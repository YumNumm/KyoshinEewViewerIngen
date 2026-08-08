using KyoshinEewViewer.Core.Models.EarthquakeReplay;

namespace KyoshinEewViewer.Series.KyoshinMonitor.Models;

/// <summary>
/// メモリ上の EEW 時系列をリプレイするためのデータ。
/// ファイルへの MessagePack 書き出しは行わないため、Core の Union には登録しない。
/// </summary>
public class InMemoryEewReplayData : ReplayData
{
	/// <summary>
	/// 再生する EEW
	/// </summary>
	public required Eew Eew { get; init; }
}
