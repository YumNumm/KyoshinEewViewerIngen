using Avalonia.Input;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Map;
using KyoshinEewViewer.Map.Layers;
using SkiaSharp;
using System;
using Location = KyoshinMonitorLib.Location;

namespace KyoshinEewViewer.Series.Earthquake;

/// <summary>
/// 震央の範囲を地図上で選択させる
/// </summary>
/// <remarks>
/// 2点のクリックで矩形を決める。<see cref="MapLayer"/> には押下･離上のフックがなく、
/// ドラッグでは地図のパン操作と競合するため。
/// 2点クリックであれば選択中も地図のパンと拡大縮小をそのまま使える
/// </remarks>
public class EpicenterRectSelectLayer : MapLayer
{
	/// <summary>
	/// 2点目が確定した
	/// </summary>
	public event Action<Location, Location>? Selected;

	private Location? _first;
	private Location? _current;

	public override bool NeedPersistentUpdate => false;

	/// <summary>
	/// 選択の状態を初期化する
	/// </summary>
	public void Reset()
	{
		_first = null;
		_current = null;
		RefreshRequest();
	}

	public override bool OnMouseClick(Location location, PointD screenPosition, MouseButton button, LayerRenderParameter param)
	{
		if (button != MouseButton.Left)
			return false;

		if (_first == null)
		{
			_first = location;
			_current = location;
			RefreshRequest();
			return true;
		}

		var first = _first;
		Reset();
		Selected?.Invoke(first, location);
		return true;
	}

	public override bool OnPointerMoved(Location location, PointD screenPosition, LayerRenderParameter param)
	{
		if (_first == null)
			return false;

		_current = location;
		RefreshRequest();
		return true;
	}

	public override void OnPointerExited()
	{
		if (_first == null)
			return;
		_current = null;
		RefreshRequest();
	}

	public override void RefreshResourceCache(WindowTheme targetControl) { }

	public override void Render(SKCanvas canvas, LayerRenderParameter param, bool isAnimating)
	{
		if (_first is not { } first || _current is not { } current)
			return;

		canvas.Save();
		try
		{
			canvas.Translate((float)-param.LeftTopPixel.X, (float)-param.LeftTopPixel.Y);

			var a = first.ToPixel(param.Zoom);
			var b = current.ToPixel(param.Zoom);
			var rect = new SKRect(
				(float)Math.Min(a.X, b.X), (float)Math.Min(a.Y, b.Y),
				(float)Math.Max(a.X, b.X), (float)Math.Max(a.Y, b.Y));

			using var fill = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(0x33, 0x99, 0xFF, 0x44) };
			using var stroke = new SKPaint
			{
				Style = SKPaintStyle.Stroke,
				Color = new SKColor(0x33, 0x99, 0xFF, 0xEE),
				StrokeWidth = 2,
				IsAntialias = true,
			};
			canvas.DrawRect(rect, fill);
			canvas.DrawRect(rect, stroke);
		}
		finally
		{
			canvas.Restore();
		}
	}
}
