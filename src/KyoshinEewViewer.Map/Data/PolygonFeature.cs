using Avalonia;
using Avalonia.Media;
using KyoshinMonitorLib;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KyoshinEewViewer.Map.Data;
public class PolygonFeature : Feature
{
	public PolygonFeature(TopologyMap map, Feature[] lineFeatures, TopologyPolygon topologyPolygon)
	{
		LineFeatures = lineFeatures;
		IsClosed = true;

		var polyIndexes = topologyPolygon.Arcs ?? throw new Exception($"マップデータがうまく読み込めていません polygonのarcsがnullです");

		PolyIndexes = polyIndexes;

#pragma warning disable CS8602, CS8604 // 高速化のためチェックをサボる
		// バウンドボックスを求めるために地理座標の計算をしておく
		var points = new List<Location>();
		foreach (var i in PolyIndexes[0])
		{
			if (points.Count == 0)
			{
				if (i < 0)
					points.AddRange(map.Arcs[Math.Abs(i) - 1].Arc.ToLocations(map).Reverse());
				else
					points.AddRange(map.Arcs[i].Arc.ToLocations(map));
				continue;
			}

			if (i < 0)
				points.AddRange(map.Arcs[Math.Abs(i) - 1].Arc.ToLocations(map).Reverse().Skip(1));
			else
				points.AddRange(map.Arcs[i].Arc.ToLocations(map).Skip(1));
		}
#pragma warning restore CS8602, CS8604

		// バウンドボックスを求める
		var minLoc = new Location(float.MaxValue, float.MaxValue);
		var maxLoc = new Location(float.MinValue, float.MinValue);
		foreach (var l in points)
		{
			minLoc.Latitude = Math.Min(minLoc.Latitude, l.Latitude);
			minLoc.Longitude = Math.Min(minLoc.Longitude, l.Longitude);

			maxLoc.Latitude = Math.Max(maxLoc.Latitude, l.Latitude);
			maxLoc.Longitude = Math.Max(maxLoc.Longitude, l.Longitude);
		}
		BB = new RectD(minLoc.CastPoint(), maxLoc.CastPoint());

		Code = topologyPolygon.Code;
	}
	private Feature[] LineFeatures { get; }
	private int[][] PolyIndexes { get; }

	public override Point[][]? GetOrCreatePointsCache(int zoom)
	{
		var pointsList = new List<List<Point>>();

		foreach (var polyIndex in PolyIndexes)
		{
			var points = new List<Point>();
			foreach (var i in polyIndex)
			{
				if (points.Count == 0)
				{
					if (i < 0)
					{
						var p = LineFeatures[Math.Abs(i) - 1].GetOrCreatePointsCache(zoom);
						if (p != null)
							points.AddRange(p[0].Reverse());
					}
					else
					{
						var p = LineFeatures[i].GetOrCreatePointsCache(zoom);
						if (p != null)
							points.AddRange(p[0]);
					}
					continue;
				}

				if (i < 0)
				{
					var p = LineFeatures[Math.Abs(i) - 1].GetOrCreatePointsCache(zoom);
					if (p != null)
						points.AddRange(p[0].Reverse().Skip(1));
				}
				else
				{
					var p = LineFeatures[i].GetOrCreatePointsCache(zoom);
					if (p != null)
						points.AddRange(p[0].Skip(1));
				}
			}
			if (points.Count > 0)
				pointsList.Add(points);
		}

		return !pointsList.Any(p => p.Any())
			? null
			: pointsList.Select(p => p.ToArray()).ToArray();
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
				for (var i = 0; i < pointsList.Length; i++)
				{
					ctx.BeginFigure(pointsList[i][0], IsClosed);
					for (var j = 1; j < pointsList[i].Length; j++)
						ctx.LineTo(pointsList[i][j]);
				}
			}
			return PathCache[zoom] = geom;
		}
		return path;
	}

	public void Draw(DrawingContext context, int zoom, Brush brush)
	{
		if (GetOrCreatePath(zoom) is Geometry path)
			context.DrawGeometry(brush, null, path);
	}
}
