using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using KyoshinEewViewer.CustomControl;
using KyoshinEewViewer.Map.Layers.ImageTile;
using KyoshinEewViewer.Map.Projections;
using System;
using System.Globalization;

namespace KyoshinEewViewer.Map.Layers;

public class ImageTileLayer : MapLayer
{
	private static readonly SolidColorBrush PlaceHolderBrush = new(Color.FromArgb(50, 255, 0, 0));
#if DEBUG
	private static readonly SolidColorBrush DebugBrush = new(Color.FromArgb(127, 255, 0, 0));
	private static readonly Pen DebugPen = new(DebugBrush);
	private static readonly Pen DebugOutlinePen = new(new SolidColorBrush(Color.FromArgb(127, 255, 255, 255))) { Thickness = 2 };
#endif

	public ImageTileProvider Provider { get; }
	public MercatorProjection MercatorProjection { get; } = new();

	public ImageTileLayer(ImageTileProvider provider)
	{
		Provider = provider;
		Provider.ImageFetched += () => RefleshRequest();
	}

	public override bool NeedPersistentUpdate => false;

	public override void RefreshResourceCache(Control targetControl) { }

	public override void Render(DrawingContext context, LayerRenderParameter param, bool isAnimating)
	{
		if (Provider.IsDisposed)
			return;
		lock (Provider)
		{
			// 使用するキャッシュのズーム
			var baseZoom = Provider.GetTileZoomLevel(param.Zoom);
			// 実際のズームに合わせるためのスケール
			var scale = Math.Pow(2, param.Zoom - baseZoom);
			var matrix  = Matrix.CreateScale(scale, scale);
			// 画面座標への変換
			var leftTop = param.LeftTopLocation.CastLocation().ToPixel(baseZoom);
			matrix = matrix.Append(Matrix.CreateTranslation(-leftTop.X, -leftTop.Y));

			using (context.PushPreTransform(matrix))
				{
					// メルカトル図法でのピクセル座標を求める
					var mercatorPixelRect = new RectD(
						param.ViewAreaRect.TopLeft.CastLocation().ToPixel(baseZoom, MercatorProjection),
						param.ViewAreaRect.BottomRight.CastLocation().ToPixel(baseZoom, MercatorProjection));

					// タイルのオフセット
					var xTileOffset = (int)(mercatorPixelRect.Left / MercatorProjection.TileSize);
					var yTileOffset = (int)(mercatorPixelRect.Top / MercatorProjection.TileSize);

					// 表示するタイルの数
					var xTileCount = (int)(mercatorPixelRect.Width / MercatorProjection.TileSize) + 2;
					var yTileCount = (int)(mercatorPixelRect.Height / MercatorProjection.TileSize) + 2;

					// タイルを描画し始める左上のピクセル座標
					var tileOrigin = new PointD(mercatorPixelRect.Left - (mercatorPixelRect.Left % MercatorProjection.TileSize), mercatorPixelRect.Top - (mercatorPixelRect.Top % MercatorProjection.TileSize));

					for (var y = 0; y < yTileCount; y++)
					{
						if (yTileOffset + y < 0)
							continue;
						var cy = (float)new PointD(0, tileOrigin.Y + y * MercatorProjection.TileSize).ToLocation(baseZoom, MercatorProjection).ToPixel(baseZoom).Y;
						var ch = (float)Math.Abs(cy - new PointD(0, tileOrigin.Y + (y + 1) * MercatorProjection.TileSize).ToLocation(baseZoom, MercatorProjection).ToPixel(baseZoom).Y);
						for (var x = 0; x < xTileCount; x++)
						{
							if (xTileOffset + x < 0)
								continue;

							var cx = (float)(tileOrigin.X + x * MercatorProjection.TileSize);
							var tx = xTileOffset + x;
							var ty = yTileOffset + y;
							if (Provider.TryGetTileBitmap(baseZoom, tx, ty, isAnimating, out var image))
							{
								if (image != null)
									context.DrawImage(image, new Rect(cx, cy, MercatorProjection.TileSize, ch));

#if DEBUG
								var posGeometry = new FormattedText($"Z{baseZoom} {{{xTileOffset + x}, {yTileOffset + y}}}", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, FixedObjectRenderer.AvaloniaMainTypeface, 12, DebugBrush).BuildGeometry(new Point(cx, cy));
								if (posGeometry != null)
									context.DrawGeometry(DebugBrush, DebugOutlinePen, posGeometry);
#endif
							}
							// -1 ズーム倍率へのフォールバック
							else if (Provider.TryGetTileBitmap(baseZoom - 1, tx / 2, ty / 2, true, out image))
							{
								var halfTile = MercatorProjection.TileSize / 2;
								var xf = tx % 2 * halfTile;
								var yf = ty % 2 * halfTile;
								if (image != null)
									context.DrawImage(image, new Rect(xf, yf, halfTile, halfTile));
							}
							else
							{
#if DEBUG
								context.DrawLine(DebugPen, new Point(cx, cy), new Point(cx, cy + ch - 2));
								context.DrawLine(DebugPen, new Point(cx, cy), new Point(cx + MercatorProjection.TileSize - 2, cy));
#endif
								context.DrawRectangle(PlaceHolderBrush, null, new Rect(cx, cy, MercatorProjection.TileSize, ch));
							}
						}
					}
				}
		}
	}
}
