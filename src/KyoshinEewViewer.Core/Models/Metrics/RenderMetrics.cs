using System;
using System.Collections.Generic;

namespace KyoshinEewViewer.Core.Models.Metrics;

/// <summary>
/// 個別レイヤーの描画メトリクス
/// </summary>
public class LayerRenderMetrics
{
	/// <summary>
	/// レイヤー名
	/// </summary>
	public required string LayerName { get; init; }

	/// <summary>
	/// 描画処理時間
	/// </summary>
	public required TimeSpan RenderTime { get; init; }

	/// <summary>
	/// 描画内容情報（KeyValueペア）
	/// </summary>
	public IReadOnlyDictionary<string, object?>? RenderInfo { get; init; }

	/// <summary>
	/// メトリクス取得時刻
	/// </summary>
	public required DateTime Timestamp { get; init; }
}

/// <summary>
/// フレーム全体の描画メトリクス
/// </summary>
public class FrameRenderMetrics
{
	/// <summary>
	/// フレーム全体の処理時間
	/// </summary>
	public required TimeSpan TotalFrameTime { get; init; }

	/// <summary>
	/// 各レイヤーのメトリクス
	/// </summary>
	public required IReadOnlyList<LayerRenderMetrics> LayerMetrics { get; init; }

	/// <summary>
	/// メトリクス取得時刻
	/// </summary>
	public required DateTime Timestamp { get; init; }

	/// <summary>
	/// ナビゲーション中かどうか
	/// </summary>
	public bool IsNavigating { get; init; }

	/// <summary>
	/// ズームレベル
	/// </summary>
	public double Zoom { get; init; }

	/// <summary>
	/// 左上の位置（緯度経度）
	/// </summary>
	public PointD LeftTopLocation { get; init; }

	/// <summary>
	/// 左上のピクセル座標
	/// </summary>
	public PointD LeftTopPixel { get; init; }

	/// <summary>
	/// ピクセル境界
	/// </summary>
	public RectD PixelBound { get; init; }

	/// <summary>
	/// 表示エリアの矩形
	/// </summary>
	public RectD ViewAreaRect { get; init; }
}

/// <summary>
/// 一定期間分をまとめたフレーム統計
/// レイヤーごとの内訳を持たない軽量な集計で、常時表示するオーバーレイ向け
/// </summary>
public sealed class FrameStatistics
{
	/// <summary>
	/// 集計期間中の平均フレームレート
	/// </summary>
	public required double Fps { get; init; }

	/// <summary>
	/// 1フレームあたりの平均描画時間 (ミリ秒)
	/// </summary>
	public required double AverageRenderTimeMs { get; init; }

	/// <summary>
	/// 集計期間中で最も遅かったフレームの描画時間 (ミリ秒)
	/// </summary>
	public required double MaxRenderTimeMs { get; init; }

	/// <summary>
	/// フレーム開始間隔の平均 (ミリ秒)
	/// </summary>
	public required double AverageIntervalMs { get; init; }

	/// <summary>
	/// 描画中のレイヤー数
	/// </summary>
	public required int LayerCount { get; init; }

	/// <summary>
	/// ズームレベル
	/// </summary>
	public required double Zoom { get; init; }

	/// <summary>
	/// 実ピクセルでの描画幅
	/// </summary>
	public required int RenderWidth { get; init; }

	/// <summary>
	/// 実ピクセルでの描画高さ
	/// </summary>
	public required int RenderHeight { get; init; }

	/// <summary>
	/// 論理ピクセルに対する実ピクセルの倍率
	/// </summary>
	public required double RenderScaling { get; init; }

	/// <summary>
	/// 継続的な再描画を要求している状態かどうか
	/// アニメーションも更新要求もない間はフレームレートが 0 になるため、その区別に使う
	/// </summary>
	public required bool IsContinuousRendering { get; init; }

	/// <summary>
	/// 直近フレームの描画時間履歴 (ミリ秒、古い順)
	/// </summary>
	public required float[] RenderTimeHistoryMs { get; init; }
}
