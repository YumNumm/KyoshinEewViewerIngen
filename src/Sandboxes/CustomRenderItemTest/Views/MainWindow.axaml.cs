using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using KyoshinEewViewer.CustomControl;
using KyoshinEewViewer.Map;
using KyoshinEewViewer.Map.Data;
using KyoshinEewViewer.Map.Layers;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace CustomRenderItemTest.Views;

public class MainWindow : Window
{
	public MainWindow()
	{
		InitializeComponent();
#if DEBUG
		this.AttachDevTools();
#endif
	}

	private Dictionary<IPointer, Point> StartPoints { get; } = new();

	double GetLength(Point p)
		=> Math.Sqrt(p.X * p.X + p.Y * p.Y);

	private void InitializeComponent()
	{
		AvaloniaXamlLoader.Load(this);

		var listMode = this.FindControl<ComboBox>("listMode")!;
		listMode.Items = Enum.GetValues(typeof(RealtimeDataRenderMode));
		listMode.SelectedIndex = 0;

		var map = this.FindControl<MapControl>("map")!;
		App.Selector?.WhenAnyValue(x => x.SelectedWindowTheme).Where(x => x != null)
				.Subscribe(x => map.RefreshResourceCache());
		KyoshinMonitorLib.Location GetLocation(Point p)
		{
			var centerPix = map!.CenterLocation.ToPixel(map.Zoom);
			var originPix = new PointD(centerPix.X + ((map.PaddedRect.Width / 2) - p.X) + map.PaddedRect.Left, centerPix.Y + ((map.PaddedRect.Height / 2) - p.Y) + map.PaddedRect.Top);
			return originPix.ToLocation(map.Zoom);
		}
		map.PointerPressed += (s, e) =>
		{
			var originPos = e.GetCurrentPoint(map).Position;
			StartPoints.Add(e.Pointer, originPos);
			// 3点以上の場合は2点になるようにする
			if (StartPoints.Count > 2)
				foreach (var pointer in StartPoints.Where(p => p.Key != e.Pointer).Select(p => p.Key).ToArray())
				{
					if (StartPoints.Count <= 2)
						break;
					StartPoints.Remove(pointer);
				}
		};
		map.PointerMoved += (s, e) =>
		{
			if (!StartPoints.ContainsKey(e.Pointer))
				return;
			var newPosition = e.GetCurrentPoint(map).Position;
			var beforePoint = StartPoints[e.Pointer];
			var vector = beforePoint - newPosition;
			if (vector.IsDefault)
				return;
			StartPoints[e.Pointer] = newPosition;

			if (StartPoints.Count <= 1)
				map.CenterLocation = (map.CenterLocation.ToPixel(map.Zoom) + (PointD)vector).ToLocation(map.Zoom);
			else
			{
				var lockPos = StartPoints.First(p => p.Key != e.Pointer).Value;

				var befLen = GetLength(lockPos - beforePoint);
				var newLen = GetLength(lockPos - newPosition);
				var lockLoc = GetLocation(lockPos);

				var df = (befLen > newLen ? -1 : 1) * GetLength(vector) * .01;
				if (Math.Abs(df) < .02)
				{
					map.CenterLocation = (map.CenterLocation.ToPixel(map.Zoom) + (PointD)vector).ToLocation(map.Zoom);
					return;
				}
				map.Zoom += df;
				Debug.WriteLine("複数移動 " + df);

				var newCenterPix = map.CenterLocation.ToPixel(map.Zoom);
				var goalOriginPix = lockLoc.ToPixel(map.Zoom);

				var paddedRect = map.PaddedRect;
				var newMousePix = new PointD(newCenterPix.X + ((paddedRect.Width / 2) - lockPos.X) + paddedRect.Left, newCenterPix.Y + ((paddedRect.Height / 2) - lockPos.Y) + paddedRect.Top);
				map.CenterLocation = (newCenterPix - (goalOriginPix - newMousePix)).ToLocation(map.Zoom);
			}
		};
		map.PointerReleased += (s, e) => StartPoints.Remove(e.Pointer);
		map.PointerWheelChanged += (s, e) =>
		{
			var mousePos = e.GetCurrentPoint(map).Position;
			var mouseLoc = GetLocation(mousePos);

			var newZoom = Math.Clamp(map.Zoom + e.Delta.Y * 0.25, map.MinZoom, map.MaxZoom);

			var newCenterPix = map.CenterLocation.ToPixel(newZoom);
			var goalMousePix = mouseLoc.ToPixel(newZoom);

			var paddedRect = map.PaddedRect;
			var newMousePix = new PointD(newCenterPix.X + ((paddedRect.Width / 2) - mousePos.X) + paddedRect.Left, newCenterPix.Y + ((paddedRect.Height / 2) - mousePos.Y) + paddedRect.Top);

			map.Zoom = newZoom;
			map.CenterLocation = (newCenterPix - (goalMousePix - newMousePix)).ToLocation(newZoom);
		};

		map.Zoom = 6;
		map.CenterLocation = new KyoshinMonitorLib.Location(36.474f, 135.264f);

		Task.Run(async () =>
		{
			var mapData = await MapData.LoadDefaultMapAsync();
			var landLayer = new LandLayer { Map = mapData };
			var landBorderLayer = new LandBorderLayer { Map = mapData };
			map.Layers = new MapLayer[] {
				landLayer,
				landBorderLayer,
				new GridLayer(),
			};
		});
	}
}
