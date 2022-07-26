using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Skia;
using KyoshinEewViewer.Map.Data;
using System;
using System.Collections.Generic;

namespace KyoshinEewViewer.Map.Layers;

public sealed class LandLayer : MapLayer
{
	public override bool NeedPersistentUpdate => false;

	public Dictionary<LandLayerType, Dictionary<int, Color>>? CustomColorMap { get; set; }

	private MapData? map;
	public MapData? Map
	{
		get => map;
		set {
			map = value;
			RefleshRequest();
		}
	}

	#region ResourceCache
	private SolidColorBrush LandFill { get; set; } = new(new Color(255, 242, 239, 233));
	//private SKPaint SpecialLandFill { get; set; } = new SKPaint
	//{
	//	Style = SKPaintStyle.Fill,
	//	Color = new SKColor(242, 239, 233),
	//};
	private SolidColorBrush OverSeasLandFill { get; set; } = new(new Color(255, 169, 169, 169));

	public override void RefreshResourceCache(Control targetControl)
	{
		Color FindColorResource(string name)
			=> (Color)(targetControl.FindResource(name) ?? throw new Exception($"マップリソース {name} が見つかりませんでした"));

		LandFill = new(FindColorResource("LandColor"));

		//SpecialLandFill = new SKPaint
		//{
		//	Style = SKPaintStyle.Stroke,
		//	Color = SKColors.Red,
		//	MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3),
		//	StrokeWidth = 5,
		//	IsAntialias = true,
		//};

		OverSeasLandFill = new(FindColorResource("OverseasLandColor"));
	}
	#endregion
	public override void Render(DrawingContext context, LayerRenderParameter param, bool isAnimating)
	{
		// コントローラーの初期化ができていなければスキップ
		if (Map == null)
			return;

		// 使用するキャッシュのズーム
		var baseZoom = (int)Math.Ceiling(param.Zoom);
		// 実際のズームに合わせるためのスケール
		var scale = Math.Pow(2, param.Zoom - baseZoom);
		var matrix = Matrix.CreateScale(scale, scale);
		// 画面座標への変換
		var leftTop = param.LeftTopLocation.CastLocation().ToPixel(baseZoom);
		matrix = matrix.Append(Matrix.CreateTranslation(-leftTop.X, -leftTop.Y));

		// 使用するレイヤー決定
		var useLayerType = LandLayerType.EarthquakeInformationSubdivisionArea;
		if (baseZoom > 10)
			useLayerType = LandLayerType.MunicipalityEarthquakeTsunamiArea;

		using (context.PushPreTransform(matrix))
		{
			// スケールに合わせてブラシのサイズ変更
			//SpecialLandFill.StrokeWidth = (float)(5 / scale);

			if (!Map.TryGetLayer(useLayerType, out var layer))
				return;

			RenderRect(param.ViewAreaRect);
			// 左右に途切れないように補完して描画させる
			if (param.ViewAreaRect.Bottom > 180)
			{
				var matrix2 = Matrix.CreateTranslation(new KyoshinMonitorLib.Location(0, 180).ToPixel(baseZoom).X, 0);
				using (context.PushPreTransform(matrix2))
				{
					var fixedRect = param.ViewAreaRect;
					fixedRect.Y -= 360;

					RenderRect(fixedRect);
				}
			}
			else if (param.ViewAreaRect.Top < -180)
			{
				var matrix2 = Matrix.CreateTranslation(-new KyoshinMonitorLib.Location(0, 180).ToPixel(baseZoom).X, 0);
				using (context.PushPreTransform(matrix2))
				{
					var fixedRect = param.ViewAreaRect;
					fixedRect.Y += 360;

					RenderRect(fixedRect);
				}
			}

			void RenderRect(RectD subViewArea)
			{
				// とりあえず海外の描画を行う
				RenderOverseas(context, baseZoom, subViewArea);

				foreach (var f in layer.FindPolygon(subViewArea))
				{
					if (CustomColorMap != null &&
						CustomColorMap.TryGetValue(useLayerType, out var map) &&
						map.TryGetValue(f.Code ?? -1, out var color))
					{
						var oc = LandFill.Color;
						LandFill.Color = color;
						f.Draw(context, baseZoom, LandFill);
						LandFill.Color = oc;
					}
					else
						f.Draw(context, baseZoom, LandFill);
				}

				if (CustomColorMap is Dictionary<LandLayerType, Dictionary<int, Color>> colorMap)
					foreach (var cLayerType in colorMap.Keys)
						if (cLayerType != useLayerType && Map.TryGetLayer(cLayerType, out var clayer))
							foreach (var f in clayer.FindPolygon(subViewArea))
								if (colorMap[cLayerType].TryGetValue(f.Code ?? -1, out var color))
								{
									f.Draw(context, baseZoom, new SolidColorBrush(color));

									//var path = f.GetOrCreatePath(baseZoom);
									//if (path == null)
									//	continue;
									//var oc = SpecialLandFill.Color;
									//SpecialLandFill.Color = color;

									//canvas.Save();
									//canvas.ClipPath(path);
									//canvas.DrawPath(path, SpecialLandFill);
									//canvas.Restore();

									//SpecialLandFill.Color = oc;
								}
			}
		}
	}
	/// <summary>
	/// 海外を描画する
	/// </summary>
	private void RenderOverseas(DrawingContext context, int baseZoom, RectD subViewArea)
	{
		if (!(Map?.TryGetLayer(LandLayerType.WorldWithoutJapan, out var layer) ?? false))
			return;

		foreach (var f in layer.FindPolygon(subViewArea))
			f.Draw(context, baseZoom, OverSeasLandFill);
	}
}
