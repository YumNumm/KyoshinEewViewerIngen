using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Skia;
using KyoshinEewViewer.Map.Data;
using System;

namespace KyoshinEewViewer.Map.Layers;

public class LandBorderLayer : MapLayer
{
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
	private Pen CoastlineStroke { get; set; } = new();
	private double CoastlineStrokeWidth { get; set; } = 1;
	private Pen PrefStroke { get; set; } = new();
	private double PrefStrokeWidth { get; set; } = .8;
	private Pen AreaStroke { get; set; } = new();
	private double AreaStrokeWidth { get; set; } = .4;

	private bool InvalidateLandStroke => CoastlineStrokeWidth <= 0;
	private bool InvalidatePrefStroke => PrefStrokeWidth <= 0;
	private bool InvalidateAreaStroke => AreaStrokeWidth <= 0;

	public override void RefreshResourceCache(Control targetControl)
	{
		Color FindColorResource(string name)
			=> (Color)(targetControl.FindResource(name) ?? throw new Exception($"マップリソース {name} が見つかりませんでした"));
		float FindFloatResource(string name)
			=> (float)(targetControl.FindResource(name) ?? throw new Exception($"マップリソース {name} が見つかりませんでした"));

		CoastlineStroke = new(new SolidColorBrush(FindColorResource("LandStrokeColor")), FindFloatResource("LandStrokeThickness"));
		CoastlineStrokeWidth = CoastlineStroke.Thickness;

		PrefStroke = new(new SolidColorBrush(FindColorResource("PrefStrokeColor")), FindFloatResource("PrefStrokeThickness"));
		PrefStrokeWidth = PrefStroke.Thickness;

		AreaStroke = new(new SolidColorBrush(FindColorResource("AreaStrokeColor")), FindFloatResource("AreaStrokeThickness"));
		AreaStrokeWidth = AreaStroke.Thickness;
	}
	#endregion

	public override bool NeedPersistentUpdate => false;

	public override void Render(DrawingContext context, LayerRenderParameter param, bool isAnimating)
	{
		// マップの初期化ができていなければスキップ
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

		using (context.PushPreTransform(matrix))
		{

			// 使用するレイヤー決定
			var useLayerType = LandLayerType.EarthquakeInformationSubdivisionArea;
			if (baseZoom > 10)
				useLayerType = LandLayerType.MunicipalityEarthquakeTsunamiArea;

			// スケールに合わせてブラシのサイズ変更
			CoastlineStroke.Thickness = CoastlineStrokeWidth / scale;
			PrefStroke.Thickness = PrefStrokeWidth / scale;
			AreaStroke.Thickness = AreaStrokeWidth / scale;

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
				for(var i = 0; i < layer.LineFeatures.Length; i++)
				{
					var f = layer.LineFeatures[i];
					if (!subViewArea.IntersectsWith(f.BB))
						continue;
					switch (f.Type)
					{
						case PolylineType.AdminBoundary:
							if (!InvalidatePrefStroke && baseZoom > 4.5)
								f.Draw(context, baseZoom, PrefStroke);
							break;
						case PolylineType.Coastline:
							if (!InvalidateLandStroke && baseZoom > 4.5)
								f.Draw(context, baseZoom, CoastlineStroke);
							break;
						case PolylineType.AreaBoundary:
							if (!InvalidateAreaStroke && baseZoom > 4.5)
								f.Draw(context, baseZoom, AreaStroke);
							break;
					}
				}
			}
		}
	}
}
