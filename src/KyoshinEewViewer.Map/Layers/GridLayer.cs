using Avalonia;
using Avalonia.Media;
using KyoshinEewViewer.CustomControl;
using KyoshinMonitorLib;
using System;
using System.Globalization;

namespace KyoshinEewViewer.Map.Layers;

public class GridLayer : MapLayer
{
	private static readonly SolidColorBrush GridBrush = new(new Color(100, 100, 100, 100));
	private static readonly Pen GridPen = new(GridBrush);

	private const float LatInterval = 5;
	private const float LngInterval = 5;

	private const double TextSize = 12;

	public override bool NeedPersistentUpdate => false;

	public override void RefreshResourceCache(Avalonia.Controls.Control targetControl) { }

	public override void Render(DrawingContext context, LayerRenderParameter param, bool isAnimating)
	{
		{
			var origin = param.ViewAreaRect.Left - (param.ViewAreaRect.Left % LatInterval);
			var count = (int)Math.Ceiling(param.ViewAreaRect.Width / LatInterval) + 1;

			for (var i = 0; i < count; i++)
			{
				var lat = origin + LatInterval * i;
				if (Math.Abs(lat) > 90)
					continue;
				var pix = new Location((float)lat, 0).ToPixel(param.Zoom);
				var h = pix.Y - param.LeftTopPixel.Y;
				context.DrawLine(GridPen, new Point(0, h), new Point(param.PixelBound.Width, h));
				var latText = new FormattedText(lat.ToString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, FixedObjectRenderer.AvaloniaMainTypeface, TextSize, GridBrush);
				context.DrawText(latText, new Point(param.Padding.Left, h));
			}
		}
		{
			var origin = param.ViewAreaRect.Top - (param.ViewAreaRect.Top % LngInterval);
			var count = (int)Math.Ceiling(param.ViewAreaRect.Height / LngInterval) + 1;

			for (var i = 0; i < count; i++)
			{
				var lng = origin + LngInterval * i;
				var pix = new Location(0, (float)lng).ToPixel(param.Zoom);
				var w = pix.X - param.LeftTopPixel.X;
				context.DrawLine(GridPen, new Point(w, 0), new Point(w, param.PixelBound.Height));
				if (lng > 180)
					lng -= 360;
				if (lng < -180)
					lng += 360;
				var lngText = new FormattedText(lng.ToString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, FixedObjectRenderer.AvaloniaMainTypeface, TextSize, GridBrush);
				context.DrawText(lngText, new Point(w, param.PixelBound.Height - TextSize - param.Padding.Bottom));
			}
		}
	}
}
