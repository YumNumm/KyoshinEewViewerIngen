using Avalonia;
using Avalonia.Media;
using System.Collections.Generic;

namespace KyoshinEewViewer.Map.Data;
public abstract class Feature
{
	public RectD BB { get; protected set; }
	public bool IsClosed { get; protected set; }

	public int? Code { get; protected set; }

	protected Dictionary<int, Geometry> PathCache { get; } = new();

	public abstract Geometry? GetOrCreatePath(int zoom);

	public abstract Point[][]? GetOrCreatePointsCache(int zoom);

	public void ClearCache() => PathCache.Clear();

	~Feature()
	{
		ClearCache();
	}
}
