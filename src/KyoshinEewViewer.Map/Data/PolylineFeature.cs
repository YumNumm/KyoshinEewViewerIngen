using Avalonia;
using Avalonia.Media;
using KyoshinMonitorLib;
using SkiaSharp;
using System;

namespace KyoshinEewViewer.Map.Data;
public class PolylineFeature : Feature
{
	public PolylineFeature(TopologyMap map, int index)
	{
		var arc = map.Arcs?[index] ?? throw new Exception($"マップデータがうまく読み込めていません arc {index} が取得できませんでした");

		Type = arc.Type switch
		{
			TopologyArcType.Coastline => PolylineType.Coastline,
			TopologyArcType.Admin => PolylineType.AdminBoundary,
			TopologyArcType.Area => PolylineType.AreaBoundary,
			_ => throw new NotImplementedException("未定義のTopologyArcTypeです"),
		};
		Points = arc.Arc?.ToLocations(map) ?? throw new Exception($"マップデータがうまく読み込めていません arc {index} がnullです");
		IsClosed =
			Math.Abs(Points[0].Latitude - Points[^1].Latitude) < 0.001 &&
			Math.Abs(Points[0].Longitude - Points[^1].Longitude) < 0.001;

		// バウンドボックスを求める
		var minLoc = new Location(float.MaxValue, float.MaxValue);
		var maxLoc = new Location(float.MinValue, float.MinValue);
		foreach (var l in Points)
		{
			minLoc.Latitude = Math.Min(minLoc.Latitude, l.Latitude);
			minLoc.Longitude = Math.Min(minLoc.Longitude, l.Longitude);

			maxLoc.Latitude = Math.Max(maxLoc.Latitude, l.Latitude);
			maxLoc.Longitude = Math.Max(maxLoc.Longitude, l.Longitude);
		}
		BB = new RectD(minLoc.CastPoint(), maxLoc.CastPoint());
	}
	private Location[] Points { get; }
	public PolylineType Type { get; }

	public override Point[][]? GetOrCreatePointsCache(int zoom)
	{
		var p = Points.ToPixedAndRedction(zoom, IsClosed);
		return p == null ? null : new[] { p };
	}

	public override Geometry? GetOrCreatePath(int zoom)
	{
		if (!PathCache.TryGetValue(zoom, out var path))
		{
			var pointsList = GetOrCreatePointsCache(zoom);
			if (pointsList == null)
				return null;
			var geom = new StreamGeometry();
			using (var ctx = geom.Open())
			{
				ctx.SetFillRule(FillRule.EvenOdd);
				for (var i = 0; i < pointsList.Length; i++) {
					ctx.BeginFigure(pointsList[i][0], IsClosed);
					for (var j = 1; j < pointsList[i].Length; j++)
						ctx.LineTo(pointsList[i][j]);
				}
			}
			return PathCache[zoom] = geom;
		}
		return path;
	}

	public void Draw(DrawingContext context, int zoom, Pen pen)
	{
		if (GetOrCreatePath(zoom) is Geometry path)
			context.DrawGeometry(null, pen, path);
	}
}
public enum PolylineType
{
	/// <summary>
	/// 海岸線
	/// </summary>
	Coastline,
	/// <summary>
	/// 行政境界
	/// </summary>
	AdminBoundary,
	/// <summary>
	/// サブ行政境界(市区町村)
	/// </summary>
	AreaBoundary,
}
