using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KyoshinEewViewer.Map.Data;

public class MapData
{
	private Dictionary<LandLayerType, FeatureLayer> Layers { get; } = [];
	protected Timer CacheClearTimer { get; }

	public MapData()
	{
		CacheClearTimer = new(s =>
		{
			lock (this)
				foreach (var l in Layers.Values)
					l.ClearCache();
		}, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
	}

	public bool TryGetLayer(LandLayerType layerType, out FeatureLayer layer)
		=> Layers.TryGetValue(layerType, out layer!);

	public static async Task<MapData> LoadDefaultMapAsync()
	{
		var mapData = new MapData();
		// 処理が重めなので雑に裏で
		await Task.Run(() =>
		{
			Console.WriteLine("********** [map]Loading map data... ********** ");
			Console.WriteLine(Properties.Resources.DefaultMap);
			try
			{
				var collection = TopologyMap.LoadCollection(Properties.Resources.DefaultMap);
				Console.WriteLine("********** [map]Loaded map data... ********** ");
				Console.WriteLine(collection.ToString());
				// NOTE: とりあえず必要な分だけインスタンスを生成
				mapData.Layers[LandLayerType.WorldWithoutJapan] = new(collection[(int)LandLayerType.WorldWithoutJapan]);
				mapData.Layers[LandLayerType.MunicipalityEarthquakeTsunamiArea] = new(collection[(int)LandLayerType.MunicipalityEarthquakeTsunamiArea]);
				mapData.Layers[LandLayerType.EarthquakeInformationSubdivisionArea] = new(collection[(int)LandLayerType.EarthquakeInformationSubdivisionArea]);
				mapData.Layers[LandLayerType.TsunamiForecastArea] = new(collection[(int)LandLayerType.TsunamiForecastArea]);
			}
			catch (Exception e)
			{
				Console.WriteLine("********** [map]Failed to load map data... ********** ");
				Console.WriteLine(e);
			}
		});

		return mapData;
	}
}
