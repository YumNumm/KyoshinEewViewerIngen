using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using KyoshinEewViewer.CustomControl;
using KyoshinEewViewer.Map;
using KyoshinEewViewer.Map.Data;
using KyoshinEewViewer.Map.Layers;
using ReactiveUI;
using System;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace CustomRenderItemTest.Views;

public class MainWindow : Window
{
	private Point _prevPos;

	public MainWindow()
	{
		InitializeComponent();
#if DEBUG
		this.AttachDevTools();
#endif
	}

	private void InitializeComponent()
	{
		AvaloniaXamlLoader.Load(this);


		AddHandler(Gestures.PointerTouchPadGestureMagnifyEvent, (s, e) =>
		{
			Debug.WriteLine("Magnify: " + e.Delta.Length);
		});

		var listMode = this.FindControl<ComboBox>("listMode");
		listMode.Items = Enum.GetValues(typeof(RealtimeDataRenderMode));
		listMode.SelectedIndex = 0;

		var map = this.FindControl<MapControl>("map");
		App.Selector?.WhenAnyValue(x => x.SelectedWindowTheme).Where(x => x != null)
				.Subscribe(x => map.RefreshResourceCache());
		map.PointerMoved += (s, e2) =>
		{
			//if (mapControl1.IsNavigating)
			//	return;
			var pointer = e2.GetCurrentPoint(map);
			var curPos = pointer.Position;
			if (pointer.Properties.IsLeftButtonPressed)
			{
				var diff = new PointD(_prevPos.X - curPos.X, _prevPos.Y - curPos.Y);
				map.CenterLocation = (map.CenterLocation.ToPixel(map.Zoom) + diff).ToLocation(map.Zoom);
			}

			_prevPos = curPos;
			//var rect = map.PaddedRect;

			//var centerPos = map.CenterLocation.ToPixel(map.Zoom);
			//var mouseLoc = new PointD(centerPos.X + ((rect.Width / 2) - curPos.X) + rect.Left, centerPos.Y + ((rect.Height / 2) - curPos.Y) + rect.Top).ToLocation(mapControl1.Projection, mapControl1.Zoom);

			//label1.Text = $"Mouse Lat: {mouseLoc.Latitude:0.000000} / Lng: {mouseLoc.Longitude:0.000000}";
		};
		map.PointerPressed += (s, e2) =>
		{
			var pointer = e2.GetCurrentPoint(map);
			if (pointer.Properties.IsLeftButtonPressed)
				_prevPos = pointer.Position;
		};
		map.PointerWheelChanged += (s, e) =>
		{
			var pointer = e.GetCurrentPoint(map);
			var paddedRect = map.PaddedRect;
			var centerPix = map.CenterLocation.ToPixel(map.Zoom);
			var mousePos = pointer.Position;
			var mousePix = new PointD(centerPix.X + ((paddedRect.Width / 2) - mousePos.X) + paddedRect.Left, centerPix.Y + ((paddedRect.Height / 2) - mousePos.Y) + paddedRect.Top);
			var mouseLoc = mousePix.ToLocation(map.Zoom);

			map.Zoom += e.Delta.Y * 0.25;

			var newCenterPix = map.CenterLocation.ToPixel(map.Zoom);
			var goalMousePix = mouseLoc.ToPixel(map.Zoom);

			var newMousePix = new PointD(newCenterPix.X + ((paddedRect.Width / 2) - mousePos.X) + paddedRect.Left, newCenterPix.Y + ((paddedRect.Height / 2) - mousePos.Y) + paddedRect.Top);

			map.CenterLocation = (map.CenterLocation.ToPixel(map.Zoom) - (goalMousePix - newMousePix)).ToLocation(map.Zoom);
		};

		map.Zoom = 6;
		map.CenterLocation = new KyoshinMonitorLib.Location(36.474f, 135.264f);

		//map.CustomColorMap = new();
		//map.CustomColorMap[LandLayerType.EarthquakeInformationSubdivisionArea] = new();
		//var random = new Random();
		//foreach (var p in map.Map[LandLayerType.EarthquakeInformationSubdivisionArea].Polygons ?? Array.Empty<TopologyPolygon>())
		//{
		//	if (p.Code is not int c)
		//		return;
		//	map.CustomColorMap[LandLayerType.EarthquakeInformationSubdivisionArea][c] = new SKColor((uint)random.Next(int.MinValue, int.MaxValue));
		//}

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
