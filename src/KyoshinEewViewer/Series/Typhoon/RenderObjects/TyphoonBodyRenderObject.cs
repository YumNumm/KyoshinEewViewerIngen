using KyoshinEewViewer.Map;
using KyoshinEewViewer.Series.Typhoon.Models;
using KyoshinMonitorLib;
using SkiaSharp;
using System;

namespace KyoshinEewViewer.Series.Typhoon.RenderObjects;

public class TyphoonBodyRenderObject(TyphoonPlace place, bool isBaseMode) : IDisposable
{
	private static readonly SKPaint CenterPaint = new()
	{
		Style = SKPaintStyle.Stroke,
		Color = SKColors.MediumAquamarine,
		StrokeWidth = 2,
		IsAntialias = true,
	};
	private static readonly SKPaint StrongPaint = new()
	{
		Style = SKPaintStyle.Stroke,
		Color = SKColors.Yellow,
		StrokeWidth = 2,
		IsAntialias = true,
	};
	private static readonly SKPaint StormPaint = new()
	{
		Style = SKPaintStyle.Stroke,
		Color = SKColors.Crimson,
		StrokeWidth = 2,
		IsAntialias = true,
	};
	private static readonly SKPaint StrongFillPaint = new()
	{
		Style = SKPaintStyle.Fill,
		Color = SKColors.Yellow.WithAlpha(50),
		StrokeWidth = 2,
	};
	private static readonly SKPaint StormFillPaint = new()
	{
		Style = SKPaintStyle.Fill,
		Color = SKColors.Crimson.WithAlpha(50),
		StrokeWidth = 2,
	};

	public bool IsBaseMode { get; set; } = isBaseMode;

	private const int CacheZoom = 5;
	private SKPath? StrongCache { get; set; }
	private SKPath? StormCache { get; set; }

	public Location OriginLocation { get; } = place.Center;
	public TyphoonRenderCircle? StrongCircle { get; } = place.Strong;
	public TyphoonRenderCircle? StormCircle { get; } = place.Storm;

	public void Render(SKCanvas canvas, double zoom)
	{
		if (IsDisposed)
			return;

		// 実際のズームに合わせるためのスケール
		var scale = Math.Pow(2, zoom - CacheZoom);

		if (IsBaseMode)
		{
			//CenterPaint.StrokeWidth = (float)(2 / scale);
			//canvas.DrawCircle(OriginLocation.ToPixel(CacheZoom).AsSKPoint(), (float)(2 / scale), CenterPaint);
			return;
		}
		// 強風域
		if (StrongCircle != null)
		{
			StrongPaint.StrokeWidth = (float)(2 / scale);
			StrongCache ??= PathGenerator.MakeCirclePath(StrongCircle.RawCenter, StrongCircle.RangeKilometer * 1000, CacheZoom, 90);

			canvas.DrawPath(StrongCache, StrongFillPaint);
			canvas.DrawPath(StrongCache, StrongPaint);
		}

		// 暴風域
		if (StormCircle != null)
		{
			StormPaint.StrokeWidth = (float)(2 / scale);
			StormCache ??= PathGenerator.MakeCirclePath(StormCircle.RawCenter, StormCircle.RangeKilometer * 1000, CacheZoom, 90);

			canvas.DrawPath(StormCache, StormFillPaint);
			canvas.DrawPath(StormCache, StormPaint);
		}

		CenterPaint.StrokeWidth = (float)(2 / scale);
		var p = OriginLocation.ToPixel(CacheZoom);
		var size = 5 / scale;
		canvas.DrawLine((p + new PointD(-size, -size)).AsSkPoint(), (p + new PointD(size, size)).AsSkPoint(), CenterPaint);
		canvas.DrawLine((p + new PointD(size, -size)).AsSkPoint(), (p + new PointD(-size, size)).AsSkPoint(), CenterPaint);
	}

	private bool IsDisposed { get; set; }

	public void Dispose()
	{
		IsDisposed = true;

		StrongCache?.Dispose();
		StrongCache = null;

		StormCache?.Dispose();
		StormCache = null;

		GC.SuppressFinalize(this);
	}
}
