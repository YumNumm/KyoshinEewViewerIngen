using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using KyoshinEewViewer.Core.Models;
using KyoshinMonitorLib;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;

namespace KyoshinEewViewer.CustomControl;

public static class FixedObjectRenderer
{
	public static readonly SKTypeface MainTypeface = SKTypeface.FromStream(AvaloniaLocator.Current.GetService<IAssetLoader>()?.Open(new Uri("avares://KyoshinEewViewer.Core/Assets/Fonts/NotoSansJP-Regular.otf", UriKind.Absolute)));

	public static readonly Typeface AvaloniaMainTypeface = new("MainFont");
	private static readonly Typeface AvaloniaIntensityTypeface = new("MainFont", weight: FontWeight.Bold);

	public const double INTENSITY_WIDE_SCALE = .75;

	public static ConcurrentDictionary<JmaIntensity, (Brush b, Brush f)> IntensityBrushCache { get; } = new();
	private static SolidColorBrush? ForegroundBrush { get; set; }
	private static SolidColorBrush? SubForegroundBrush { get; set; }

	public static bool PaintCacheInitalized { get; private set; }

	public static void UpdateIntensityPaintCache(Control control)
	{
		Color FindColorResource(string name)
			=> (Color)(control.FindResource(name) ?? throw new Exception($"震度リソース {name} が見つかりませんでした"));

		ForegroundBrush = new SolidColorBrush(FindColorResource("ForegroundColor"));
		SubForegroundBrush = new SolidColorBrush(FindColorResource("SubForegroundColor"));

		foreach (var i in Enum.GetValues<JmaIntensity>())
		{
			var bb = new SolidColorBrush(FindColorResource(i + "Background"));
			var fb = new SolidColorBrush(FindColorResource(i + "Foreground"));

			IntensityBrushCache.AddOrUpdate(i, (bb, fb), (v, c) => (bb, fb));
		}
		PaintCacheInitalized = true;
	}

	/// <summary>
	/// 震度アイコンを描画する
	/// </summary>
	/// <param name="drawingContext">描画先のDrawingContext</param>
	/// <param name="intensity">描画する震度</param>
	/// <param name="point">座標</param>
	/// <param name="size">描画するサイズ ワイドモードの場合縦サイズになる</param>
	/// <param name="centerPosition">指定した座標を中心座標にするか</param>
	/// <param name="circle">縁を円形にするか wideがfalseのときのみ有効</param>
	/// <param name="wide">ワイドモード(強弱漢字表記)にするか</param>
	/// <param name="round">縁を丸めるか wide,circleがfalseのときのみ有効</param>
	public static void DrawIntensity(this DrawingContext context, JmaIntensity intensity, Point point, double size, bool centerPosition = false, bool circle = false, bool wide = false, bool round = false)
	{
		if (!IntensityBrushCache.TryGetValue(intensity, out var paints))
			return;

		var halfSize = new PointD(size / 2, size / 2);
		if (wide)
			halfSize.X /= INTENSITY_WIDE_SCALE;
		var leftTop = centerPosition ? point - halfSize : (PointD)point;

		if (circle && !wide)
			context.DrawEllipse(paints.b, null, centerPosition ? point : (point + halfSize).AsPoint(), size / 2, size / 2);
		else if (round && !wide)
		{
			var roundAmount = Math.Min(size * .2, 8);
			context.DrawRectangle(paints.b, null, new Rect(leftTop.X, leftTop.Y, (float)(wide ? size / INTENSITY_WIDE_SCALE : size), size), roundAmount, roundAmount);
		}
		else
			context.DrawRectangle(paints.b, null, new Rect(leftTop.X, leftTop.Y, (float)(wide ? size / INTENSITY_WIDE_SCALE : size), size));

		switch (intensity)
		{
			case JmaIntensity.Int1:
				if (size >= 8)
				{
					var txt = new FormattedText(intensity.ToShortString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AvaloniaIntensityTypeface, size, paints.f);
					context.DrawText(txt, new PointD(leftTop.X + size * (wide ? .38 : .2), leftTop.Y - size * .3).AsPoint());
				}
				return;
			case JmaIntensity.Int4:
				if (size >= 8)
				{
					var txt = new FormattedText(intensity.ToShortString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AvaloniaIntensityTypeface, size, paints.f);
					context.DrawText(txt, new PointD(leftTop.X + size * (wide ? .38 : .19), leftTop.Y - size * .29).AsPoint());
				}
				return;
			case JmaIntensity.Int7:
				if (size >= 8)
				{
					var txt = new FormattedText(intensity.ToShortString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AvaloniaIntensityTypeface, size, paints.f);
					context.DrawText(txt, new PointD(leftTop.X + size * (wide ? .38 : .22), leftTop.Y - size * .28).AsPoint());
				}
				return;
			case JmaIntensity.Int5Lower:
				{
					if (size < 8)
					{
						var txt1 = new FormattedText("-", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AvaloniaIntensityTypeface, size * 1.25, paints.f);
						context.DrawText(txt1, new PointD(leftTop.X + size * .25, leftTop.Y - size * .4).AsPoint());
						break;
					}
					var txt2 = new FormattedText("5", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AvaloniaIntensityTypeface, size, paints.f);
					context.DrawText(txt2, new PointD(leftTop.X + size * .1, leftTop.Y - size * .27).AsPoint());
					if (wide)
					{
						var txt3 = new FormattedText("弱", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AvaloniaIntensityTypeface, size * .55, paints.f);
						context.DrawText(txt3, new PointD(leftTop.X + size * .65, leftTop.Y + size * .22).AsPoint());
					}
					else
					{
						var txt3 = new FormattedText("-", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AvaloniaIntensityTypeface, size, paints.f);
						context.DrawText(txt3, new PointD(leftTop.X + size * .6, leftTop.Y - size * .5).AsPoint());
					}
				}
				return;
			case JmaIntensity.Int5Upper:
				{
					if (size < 8)
					{
						var txt1 = new FormattedText("+", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AvaloniaIntensityTypeface, size * 1.25, paints.f);
						context.DrawText(txt1, new PointD(leftTop.X + size * .1, leftTop.Y + size * .8).AsPoint());
						break;
					}
					var txt2 = new FormattedText("5", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AvaloniaIntensityTypeface, size, paints.f);
					context.DrawText(txt2, new PointD(leftTop.X + size * .1, leftTop.Y - size * .27).AsPoint());
					if (wide)
					{
						var txt3 = new FormattedText("強", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AvaloniaIntensityTypeface, size * .55, paints.f);
						context.DrawText(txt3, new PointD(leftTop.X + size * .65, leftTop.Y + size * .22).AsPoint());
					}
					else
					{
						var txt3 = new FormattedText("+", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AvaloniaIntensityTypeface, size * .8, paints.f);
						context.DrawText(txt3, new PointD(leftTop.X + size * .5, leftTop.Y - size * .25).AsPoint());
					}
				}
				return;
			case JmaIntensity.Int6Lower:
				{
					if (size < 8)
					{
						var txt1 = new FormattedText("-", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AvaloniaIntensityTypeface, size * 1.25, paints.f);
						context.DrawText(txt1, new PointD(leftTop.X + size * .25, leftTop.Y + size * .8).AsPoint());
						break;
					}
					var txt2 = new FormattedText("6", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AvaloniaIntensityTypeface, size, paints.f);
					context.DrawText(txt2, new PointD(leftTop.X + size * .1, leftTop.Y - size * .29).AsPoint());
					if (wide)
					{
						var txt3 = new FormattedText("弱", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AvaloniaIntensityTypeface, size * .55, paints.f);
						context.DrawText(txt3, new PointD(leftTop.X + size * .65, leftTop.Y + size * .22).AsPoint());
					}
					else
					{
						var txt3 = new FormattedText("-", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AvaloniaIntensityTypeface, size, paints.f);
						context.DrawText(txt3, new PointD(leftTop.X + size * .6, leftTop.Y - size * .5).AsPoint());
					}
				}
				return;
			case JmaIntensity.Int6Upper:
				{
					if (size < 8)
					{
						var txt1 = new FormattedText("+", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AvaloniaIntensityTypeface, size * 1.25, paints.f);
						context.DrawText(txt1, new PointD(leftTop.X + size * .1, leftTop.Y + size * .8).AsPoint());
						break;
					}
					var txt2 = new FormattedText("6", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AvaloniaIntensityTypeface, size, paints.f);
					context.DrawText(txt2, new PointD(leftTop.X + size * .1, leftTop.Y - size * .29).AsPoint());
					if (wide)
					{
						var txt3 = new FormattedText("強", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AvaloniaIntensityTypeface, size * .55, paints.f);
						context.DrawText(txt3, new PointD(leftTop.X + size * .65, leftTop.Y + size * .22).AsPoint());
					}
					else
					{
						var txt3 = new FormattedText("+", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AvaloniaIntensityTypeface, size * .8, paints.f);
						context.DrawText(txt3, new PointD(leftTop.X + size * .5, leftTop.Y - size * .25).AsPoint());
					}
				}
				return;
			case JmaIntensity.Unknown:
				{
					var txt = new FormattedText("-", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AvaloniaIntensityTypeface, size, paints.f);
					context.DrawText(txt, new PointD(leftTop.X + size * (wide ? .52 : .32), leftTop.Y - size * .35).AsPoint());
				}
				return;
			case JmaIntensity.Error:
				{
					var txt = new FormattedText("E", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AvaloniaIntensityTypeface, size, paints.f);
					context.DrawText(txt, new PointD(leftTop.X + size * (wide ? .35 : .18), leftTop.Y - size * .29).AsPoint());
				}
				return;
			default:
				{
					if (size >= 8)
					{
						var txt = new FormattedText(intensity.ToShortString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AvaloniaIntensityTypeface, size, paints.f);
						context.DrawText(txt, new PointD(leftTop.X + size * (wide ? .38 : .22), leftTop.Y - size * .29).AsPoint());
					}
				}
				return;
		}
	}

	public static void DrawLinkedRealtimeData(this DrawingContext context, IEnumerable<RealtimeObservationPoint>? points, double height, double maxWidth, double maxHeight, RealtimeDataRenderMode mode)
	{
		if (points == null || ForegroundBrush == null || SubForegroundBrush == null) return;

		var count = 0;
		var verticalOffset = 0.0;
		foreach (var point in points)
		{
			var horizontalOffset = 0.0;
			switch (mode)
			{
				case RealtimeDataRenderMode.ShindoIconAndRawColor:
					if (point.LatestIntensity.ToJmaIntensity() >= JmaIntensity.Int1)
						goto case RealtimeDataRenderMode.ShindoIcon;
					goto case RealtimeDataRenderMode.RawColor;
				case RealtimeDataRenderMode.ShindoIconAndMonoColor:
					if (point.LatestIntensity.ToJmaIntensity() >= JmaIntensity.Int1)
						goto case RealtimeDataRenderMode.ShindoIcon;
					{
						if (point.LatestColor is SKColor color)
						{
							var num = (byte)(color.Red / 3 + color.Green / 3 + color.Blue / 3);
							var rectBrush = new SolidColorBrush(new Color(255, num, num, num));
							context.DrawRectangle(rectBrush, null, new Rect(0, verticalOffset, height / 5, height));
						}
						horizontalOffset += height / 5;
					}
					break;
				case RealtimeDataRenderMode.ShindoIcon:
					context.DrawIntensity(point.LatestIntensity.ToJmaIntensity(), new Point(0, verticalOffset), height);
					horizontalOffset += height;
					break;
				case RealtimeDataRenderMode.WideShindoIcon:
					context.DrawIntensity(point.LatestIntensity.ToJmaIntensity(), new Point(0, verticalOffset), height, wide: true);
					horizontalOffset += height * 1.25f;
					break;
				case RealtimeDataRenderMode.RawColor:
					{
						if (point.LatestColor is SKColor color)
						{
							var rectBrush = new SolidColorBrush(new Color(255, color.Red, color.Green, color.Blue));
							context.DrawRectangle(rectBrush, null, new Rect(0, verticalOffset, height / 5, height));
						}
						horizontalOffset += height / 5;
					}
					break;
			}

			var region = point.Region;
			if (region.Length > 3)
				region = region[..3];

#if DEBUG
			var prevColor = ForegroundBrush.Color;
			if (point.Event != null)
				ForegroundBrush.Color = new Color(255, point.Event.DebugColor.Red, point.Event.DebugColor.Green, point.Event.DebugColor.Blue);
#endif

			var regionText = new FormattedText(region, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AvaloniaMainTypeface, height * .6, ForegroundBrush);
			context.DrawText(regionText, new(horizontalOffset + height * 0.1f, verticalOffset));
			horizontalOffset += Math.Max(regionText.Width, maxWidth / 4);

			var nameText = new FormattedText(point.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AvaloniaIntensityTypeface, height * .75, ForegroundBrush);
			context.DrawText(nameText, new(horizontalOffset, verticalOffset - height * .1));

			var intensityText = new FormattedText(point.LatestIntensity?.ToString("0.0") ?? "?", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AvaloniaMainTypeface, height * .6, SubForegroundBrush);
			context.DrawText(intensityText, new(maxWidth - intensityText.Width, verticalOffset + height * .2));

#if DEBUG
			ForegroundBrush.Color = prevColor;
#endif

			count++;
			verticalOffset += height;
			if (verticalOffset >= maxHeight)
				return;
		}
	}
}
