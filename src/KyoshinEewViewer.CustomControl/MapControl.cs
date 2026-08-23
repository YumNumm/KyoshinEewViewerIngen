using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Avalonia.Threading;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Core.Models.Metrics;
using KyoshinEewViewer.Map;
using KyoshinEewViewer.Map.Layers;
using KyoshinMonitorLib;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using Location = KyoshinMonitorLib.Location;

namespace KyoshinEewViewer.CustomControl;

public partial class MapControl : Avalonia.Controls.Control, ICustomHitTest
{
	private Location _centerLocation = new(36.474f, 135.264f);
	public static readonly DirectProperty<MapControl, Location> CenterLocationProperty =
		AvaloniaProperty.RegisterDirect<MapControl, Location>(
			nameof(CenterLocation),
			o => o.CenterLocation,
			(o, v) => o.CenterLocation = v
		);
	public Location CenterLocation
	{
		get => _centerLocation;
		set {
			if (!SetAndRaise(CenterLocationProperty, ref _centerLocation, value))
				return;
			if (_centerLocation != null)
			{
				var cl = _centerLocation;
				cl.Latitude = Math.Min(Math.Max(cl.Latitude, -80), 80);
				// 1回転させる
				if (cl.Longitude < -180)
					cl.Longitude += 360;
				if (cl.Longitude > 180)
					cl.Longitude -= 360;
				_centerLocation = cl;
			}

			Dispatcher.UIThread.Post(() =>
			{
				ApplySize();
				RequestRedraw();
			});
		}
	}

	private double _zoom = 4;
	public static readonly DirectProperty<MapControl, double> ZoomProperty =
		AvaloniaProperty.RegisterDirect<MapControl, double>(
			nameof(Zoom),
			o => o.Zoom,
			(o, v) => o.Zoom = v
		);
	public double Zoom
	{
		get => _zoom;
		set {
			var fb = Math.Min(Math.Max(value, MinZoom), MaxZoom);
			if (!SetAndRaise(ZoomProperty, ref _zoom, fb))
				return;
			Dispatcher.UIThread.Post(() =>
			{
				ApplySize();
				RequestRedraw();
			});
		}
	}

	private MapLayerHost LayerHost { get; } = new();

	/// <summary>
	/// 最新の描画パフォーマンスメトリクス
	/// </summary>
	private FrameRenderMetrics? _latestMetrics;
	public FrameRenderMetrics? LatestMetrics
	{
		get => _latestMetrics;
		private set => SetAndRaise(LatestMetricsProperty, ref _latestMetrics, value);
	}
	public static readonly DirectProperty<MapControl, FrameRenderMetrics?> LatestMetricsProperty =
		AvaloniaProperty.RegisterDirect<MapControl, FrameRenderMetrics?>(
			nameof(LatestMetrics),
			o => o.LatestMetrics);

	private bool _isMetricsEnabled;
	public static readonly DirectProperty<MapControl, bool> IsMetricsEnabledProperty =
		AvaloniaProperty.RegisterDirect<MapControl, bool>(
			nameof(IsMetricsEnabled),
			o => o.IsMetricsEnabled,
			(o, v) => o.IsMetricsEnabled = v
		);
	/// <summary>
	/// パフォーマンスメトリクスの収集を有効にするかどうか
	/// </summary>
	public bool IsMetricsEnabled
	{
		get => _isMetricsEnabled;
		set => SetAndRaise(IsMetricsEnabledProperty, ref _isMetricsEnabled, value);
	}

	private DateTime _lastMetricsRecordTime = DateTime.MinValue;
	private static readonly TimeSpan MetricsRecordInterval = TimeSpan.FromSeconds(0.5);

	#region フレーム統計
	private static readonly TimeSpan FrameStatisticsInterval = TimeSpan.FromMilliseconds(500);
	private const int FrameTimeHistoryLength = 120;

	private bool _isFrameStatisticsEnabled;
	public static readonly DirectProperty<MapControl, bool> IsFrameStatisticsEnabledProperty =
		AvaloniaProperty.RegisterDirect<MapControl, bool>(
			nameof(IsFrameStatisticsEnabled),
			o => o.IsFrameStatisticsEnabled,
			(o, v) => o.IsFrameStatisticsEnabled = v
		);
	/// <summary>
	/// フレーム統計の収集を有効にするかどうか
	/// </summary>
	public bool IsFrameStatisticsEnabled
	{
		get => _isFrameStatisticsEnabled;
		set => SetAndRaise(IsFrameStatisticsEnabledProperty, ref _isFrameStatisticsEnabled, value);
	}

	private FrameStatistics? _frameStatistics;
	public static readonly DirectProperty<MapControl, FrameStatistics?> FrameStatisticsProperty =
		AvaloniaProperty.RegisterDirect<MapControl, FrameStatistics?>(
			nameof(FrameStatistics),
			o => o.FrameStatistics);
	/// <summary>
	/// 直近の集計期間のフレーム統計
	/// </summary>
	public FrameStatistics? FrameStatistics
	{
		get => _frameStatistics;
		private set => SetAndRaise(FrameStatisticsProperty, ref _frameStatistics, value);
	}

	// 描画スレッドが積算し、UI スレッドのタイマーが読み出してリセットする
	private readonly Lock _frameStatsLock = new();
	private readonly float[] _frameTimeHistory = new float[FrameTimeHistoryLength];
	private int _frameTimeHistoryIndex;
	private int _frameTimeHistoryCount;
	private int _frameCount;
	private double _renderTimeSumMs;
	private double _renderTimeMaxMs;
	private double _intervalSumMs;
	private int _intervalCount;
	private long _lastFrameStartTimestamp;

	private DispatcherTimer? _frameStatisticsTimer;
	private long _frameStatisticsWindowStart;

	private static double ToMilliseconds(long timestampDelta) => timestampDelta * 1000.0 / Stopwatch.Frequency;

	/// <summary>
	/// 描画スレッドから1フレーム分の計測値を積算する
	/// </summary>
	private void AccumulateFrameStatistics(long startTimestamp, long endTimestamp)
	{
		var renderMs = ToMilliseconds(endTimestamp - startTimestamp);
		lock (_frameStatsLock)
		{
			_frameTimeHistory[_frameTimeHistoryIndex] = (float)renderMs;
			_frameTimeHistoryIndex = (_frameTimeHistoryIndex + 1) % FrameTimeHistoryLength;
			if (_frameTimeHistoryCount < FrameTimeHistoryLength)
				_frameTimeHistoryCount++;

			_frameCount++;
			_renderTimeSumMs += renderMs;
			if (renderMs > _renderTimeMaxMs)
				_renderTimeMaxMs = renderMs;

			if (_lastFrameStartTimestamp != 0)
			{
				_intervalSumMs += ToMilliseconds(startTimestamp - _lastFrameStartTimestamp);
				_intervalCount++;
			}
			_lastFrameStartTimestamp = startTimestamp;
		}
	}

	/// <summary>
	/// 積算した計測値を集計してプロパティへ反映する
	/// </summary>
	private void PublishFrameStatistics()
	{
		var now = Stopwatch.GetTimestamp();
		var elapsedMs = ToMilliseconds(now - _frameStatisticsWindowStart);
		_frameStatisticsWindowStart = now;

		float[] history;
		int frameCount, intervalCount;
		double renderTimeSum, renderTimeMax, intervalSum;
		lock (_frameStatsLock)
		{
			history = new float[_frameTimeHistoryCount];
			// リングバッファを古い順に並べ直す
			for (var i = 0; i < _frameTimeHistoryCount; i++)
				history[i] = _frameTimeHistory[(_frameTimeHistoryIndex - _frameTimeHistoryCount + i + FrameTimeHistoryLength) % FrameTimeHistoryLength];

			frameCount = _frameCount;
			renderTimeSum = _renderTimeSumMs;
			renderTimeMax = _renderTimeMaxMs;
			intervalSum = _intervalSumMs;
			intervalCount = _intervalCount;

			_frameCount = 0;
			_renderTimeSumMs = 0;
			_renderTimeMaxMs = 0;
			_intervalSumMs = 0;
			_intervalCount = 0;
			// 描画が完全に止まっていた区間をまたいだ間隔は平均を大きく歪めるため、次の再開時は間隔を計上しない
			if (frameCount == 0)
				_lastFrameStartTimestamp = 0;
		}

		var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
		FrameStatistics = new FrameStatistics
		{
			Fps = elapsedMs > 0 ? frameCount * 1000.0 / elapsedMs : 0,
			AverageRenderTimeMs = frameCount > 0 ? renderTimeSum / frameCount : 0,
			MaxRenderTimeMs = renderTimeMax,
			AverageIntervalMs = intervalCount > 0 ? intervalSum / intervalCount : 0,
			LayerCount = Layers?.Length ?? 0,
			Zoom = Zoom,
			RenderWidth = (int)Math.Round(Bounds.Width * scaling),
			RenderHeight = (int)Math.Round(Bounds.Height * scaling),
			RenderScaling = scaling,
			IsContinuousRendering = Volatile.Read(ref _needsContinuousFrame) != 0,
			RenderTimeHistoryMs = history,
		};
	}

	private void UpdateFrameStatisticsTimer()
	{
		if (IsFrameStatisticsEnabled && _customVisual is not null)
		{
			if (_frameStatisticsTimer is not null)
				return;
			_frameStatisticsWindowStart = Stopwatch.GetTimestamp();
			_frameStatisticsTimer = new DispatcherTimer(FrameStatisticsInterval, DispatcherPriority.Background, (_, _) => PublishFrameStatistics());
			_frameStatisticsTimer.Start();
			return;
		}
		_frameStatisticsTimer?.Stop();
		_frameStatisticsTimer = null;
	}
	#endregion フレーム統計

	public static readonly DirectProperty<MapControl, MapLayer[]?> LayersProperty =
		AvaloniaProperty.RegisterDirect<MapControl, MapLayer[]?>(
			nameof(Layers),
			o => o.Layers,
			(o, v) => o.Layers = v,
			null
		);
	public MapLayer[]? Layers
	{
		get => LayerHost.Layers;
		set => LayerHost.Layers = value;
	}

	private double _maxZoom = 12;
	public static readonly DirectProperty<MapControl, double> MaxZoomProperty =
		AvaloniaProperty.RegisterDirect<MapControl, double>(
			nameof(MaxZoom),
			o => o.MaxZoom,
			(o, v) => o.MaxZoom = v
		);
	public double MaxZoom
	{
		get => _maxZoom;
		set {
			SetAndRaise(MaxZoomProperty, ref _maxZoom, value);
			Zoom = _zoom;
		}
	}

	public static readonly DirectProperty<MapControl, double> MaxNavigateZoomProperty =
		AvaloniaProperty.RegisterDirect<MapControl, double>(
			nameof(MaxNavigateZoom),
			o => o.MaxNavigateZoom,
			(o, v) => o.MaxNavigateZoom = v);
	public double MaxNavigateZoom { get; set; } = 10;

	private double _minZoom = 4;
	public static readonly DirectProperty<MapControl, double> MinZoomProperty =
		AvaloniaProperty.RegisterDirect<MapControl, double>(
			nameof(MinZoom),
			o => o.MinZoom,
			(o, v) => o.MinZoom = v
		);
	public double MinZoom
	{
		get => _minZoom;
		set {
			SetAndRaise(MinZoomProperty, ref _minZoom, value);
			Zoom = _zoom;
		}
	}

	private Thickness _padding = new();
	public static readonly DirectProperty<MapControl, Thickness> PaddingProperty =
		AvaloniaProperty.RegisterDirect<MapControl, Thickness>(
			nameof(Padding),
			o => o.Padding,
			(o, v) => o.Padding = v
		);
	public Thickness Padding
	{
		get => _padding;
		set {
			SetAndRaise(PaddingProperty, ref _padding, value);

			Dispatcher.UIThread.Post(() =>
			{
				ApplySize();
				RequestRedraw();
			});
		}
	}

	public static readonly DirectProperty<MapControl, bool> IsHeadlessModeProperty =
		AvaloniaProperty.RegisterDirect<MapControl, bool>(
			nameof(IsHeadlessMode),
			o => o.IsHeadlessMode,
			(o, v) => o.IsHeadlessMode = v
		);
	private bool _isHeadlessMode = false;
	public bool IsHeadlessMode
	{
		get => _isHeadlessMode;
		set => SetAndRaise(IsHeadlessModeProperty, ref _isHeadlessMode, value);
	}

	public static readonly DirectProperty<MapControl, bool> IsDisableManualControlProperty =
		AvaloniaProperty.RegisterDirect<MapControl, bool>(
			nameof(IsDisableManualControl),
			o => o.IsDisableManualControl,
			(o, v) => o.IsDisableManualControl = v
		);
	private bool _isDisableManualControl = false;
	public bool IsDisableManualControl
	{
		get => _isDisableManualControl;
		set => SetAndRaise(IsDisableManualControlProperty, ref _isDisableManualControl, value);
	}

	public static readonly DirectProperty<MapControl, bool> IsInertiaEnabledProperty =
		AvaloniaProperty.RegisterDirect<MapControl, bool>(
			nameof(IsInertiaEnabled),
			o => o.IsInertiaEnabled,
			(o, v) => o.IsInertiaEnabled = v
		);
	private bool _isInertiaEnabled = true;
	/// <summary>
	/// 慣性スクロールを有効にするかどうか
	/// </summary>
	public bool IsInertiaEnabled
	{
		get => _isInertiaEnabled;
		set => SetAndRaise(IsInertiaEnabledProperty, ref _isInertiaEnabled, value);
	}

	#region Navigate
	private NavigateAnimation? NavigateAnimation { get; set; }
	public bool IsNavigating => NavigateAnimation?.IsRunning ?? false;

	public void Navigate(Rect bound, TimeSpan duration, bool unlimitNavigateZoom = false)
		=> Navigate(new RectD(bound.X, bound.Y, bound.Width, bound.Height), duration, unlimitNavigateZoom);
	public void Navigate(Rect bound, TimeSpan duration, Rect mustBound)
		=> Navigate(new RectD(bound.X, bound.Y, bound.Width, bound.Height), duration, new RectD(mustBound.X, mustBound.Y, mustBound.Width, mustBound.Height));

	public void Navigate(RectD bound, TimeSpan duration, RectD mustBound)
	{
		var halfRenderSize = new PointD(PaddedRect.Width / 2, PaddedRect.Height / 2);
		// 左上/右下のピクセル座標
		var leftTop = (CenterLocation.ToPixel(Zoom) - halfRenderSize).ToLocation(Zoom);
		var rightBottom = (CenterLocation.ToPixel(Zoom) + halfRenderSize).ToLocation(Zoom);

		// 今見えている範囲よりmustBoundのほうがでかい場合ナビゲーションする
		if (mustBound.Left < rightBottom.Latitude || mustBound.Right > leftTop.Latitude ||
			mustBound.Top < leftTop.Longitude || mustBound.Bottom > rightBottom.Longitude || IsHeadlessMode)
			Navigate(bound, duration, false);
	}
	// 指定した範囲をすべて表示できるように調整する
	public void Navigate(RectD bound, TimeSpan duration, bool unlimitNavigateZoom = false)
	{
		if (!Dispatcher.UIThread.CheckAccess())
		{
			Dispatcher.UIThread.Post(() => Navigate(bound, duration, unlimitNavigateZoom));
			return;
		}
		var boundPixel = new RectD(bound.TopLeft.CastLocation().ToPixel(Zoom), bound.BottomRight.CastLocation().ToPixel(Zoom));
		var centerPixel = CenterLocation.ToPixel(Zoom);
		var halfRect = PaddedRect.Size / 2;
		var leftTop = centerPixel - halfRect;
		var rightBottom = centerPixel + halfRect;
		Navigate(new NavigateAnimation(
				Zoom,
				MinZoom,
				unlimitNavigateZoom ? MaxZoom : MaxNavigateZoom,
				new RectD(leftTop, rightBottom),
				boundPixel,
				duration,
				PaddedRect));
	}
	internal void Navigate(NavigateAnimation parameter)
	{
		if (PaddedRect.Width == 0 || PaddedRect.Height == 0)
			return;

		// Navigateは慣性アニメーションより優先
		_inertiaAnimation?.Stop();
		_inertiaAnimation = null;

		if (parameter.Duration <= TimeSpan.Zero)
		{
			(Zoom, CenterLocation) = parameter.GetCurrentParameter(Zoom, PaddedRect);
			Dispatcher.UIThread.Post(() =>
			{
				RequestRedraw();
				RequestAnimationFrameIfNeeded();
			});
			return;
		}
		NavigateAnimation = parameter;
		NavigateAnimation.Start();
		Dispatcher.UIThread.Post(() =>
		{
			RequestRedraw();
			RequestAnimationFrameIfNeeded();
		});
	}

	public bool IsNavigatedPosition(RectD bound)
	{
		var boundPixel = new RectD(bound.TopLeft.CastLocation().ToPixel(Zoom), bound.BottomRight.CastLocation().ToPixel(Zoom));
		var centerPixel = CenterLocation.ToPixel(Zoom);
		var halfRect = PaddedRect.Size / 2;
		var leftTop = centerPixel - halfRect;
		var rightBottom = centerPixel + halfRect;

		var anim = new NavigateAnimation(
				Zoom,
				MinZoom,
				MaxZoom,
				new RectD(leftTop, rightBottom),
				boundPixel,
				TimeSpan.Zero,
				PaddedRect);

		var (z, c) = anim.GetCurrentParameter(Zoom, PaddedRect);

		return Math.Abs(Zoom - z) < 0.001
			&& Math.Abs(c.Latitude - CenterLocation.Latitude) < 0.001
			&& Math.Abs(c.Longitude - CenterLocation.Longitude) < 0.001;
	}
	#endregion Navigate

	public void RefreshResourceCache(WindowTheme windowTheme)
	{
		LayerHost.WindowTheme = windowTheme;
	}

	public RectD PaddedRect { get; private set; }

	private LayerRenderParameter RenderParameter { get; set; }

	// UI thread が write、render thread が read する snapshot 領域
	private readonly Lock _snapshotLock = new();
	private LayerRenderParameter _renderParamSnapshot;
	private bool _isAnimatingSnapshot;

	// render thread が write、UI thread が read
	private int _needsContinuousFrame;

	private bool _animationFramePending;
	// 地物の生成完了が集中した場合も、UI スレッドへの再描画要求は1件にまとめる
	private int _redrawRequestPending;
	private int _compositorRedrawPending;

	private CompositionCustomVisual? _customVisual;
	private MapVisualHandler? _handler;

	public MapControl()
	{
		LayerHost.RefreshRequested += RequestRedraw;
		// Visual Tree に追加されるまでは共有レイヤーからこのコントロールを参照させない
		LayerHost.Deactivate();
	}

	public bool HitTest(Point p) =>
		p.X >= 0 && p.Y >= 0 && p.X < Bounds.Width && p.Y < Bounds.Height;

	protected override void OnInitialized()
	{
		base.OnInitialized();

		ApplySize();
		RequestRedraw();
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);

		var compositor = ElementComposition.GetElementVisual(this)?.Compositor;
		if (compositor is null)
			return;

		_handler = new MapVisualHandler(this);
		_customVisual = compositor.CreateCustomVisual(_handler);
		_customVisual.Size = new Vector2((float)Bounds.Width, (float)Bounds.Height);
		ElementComposition.SetElementChildVisual(this, _customVisual);
		lock (LayerHost)
			LayerHost.Activate();

		UpdateFrameStatisticsTimer();

		ApplySize();
		RequestRedraw();
	}

	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		// detach 後に進めても意味の無いアニメーションを停止
		NavigateAnimation = null;
		_inertiaAnimation?.Stop();
		_inertiaAnimation = null;
		_wheelZoomTracker.Reset();
		lock (LayerHost)
			LayerHost.Deactivate();

		ElementComposition.SetElementChildVisual(this, null);
		_customVisual = null;
		_handler = null;
		Volatile.Write(ref _redrawRequestPending, 0);
		Volatile.Write(ref _compositorRedrawPending, 0);

		UpdateFrameStatisticsTimer();

		base.OnDetachedFromVisualTree(e);
	}

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);

		if (change.Property == BoundsProperty)
		{
			ApplySize();
			if (_customVisual is not null)
			{
				_customVisual.Size = new Vector2((float)Bounds.Width, (float)Bounds.Height);
				RequestRedraw();
			}
		}
		else if (change.Property == IsFrameStatisticsEnabledProperty)
		{
			UpdateFrameStatisticsTimer();
		}
	}

	public void RequestRedraw()
	{
		if (!Dispatcher.UIThread.CheckAccess())
		{
			if (Interlocked.Exchange(ref _redrawRequestPending, 1) != 0)
				return;
			Dispatcher.UIThread.Post(() =>
			{
				Volatile.Write(ref _redrawRequestPending, 0);
				RequestRedraw();
			});
			return;
		}
		lock (_snapshotLock)
		{
			_renderParamSnapshot = RenderParameter;
			_isAnimatingSnapshot = IsNavigating;
		}
		if (_customVisual is not { } customVisual)
			return;
		// 送信済みのメッセージが OnRender に到達するまでは、最新のスナップショットへの更新だけで十分
		if (Interlocked.Exchange(ref _compositorRedrawPending, 1) != 0)
			return;
		customVisual.SendHandlerMessage(MapVisualHandler.RedrawMessage);
	}

	private void RequestAnimationFrameIfNeeded()
	{
		Dispatcher.UIThread.VerifyAccess();
		if (_animationFramePending)
			return;
		if (_customVisual is null)
			return;
		if (TopLevel.GetTopLevel(this) is not { } tl)
			return;
		_animationFramePending = true;
		tl.RequestAnimationFrame(OnAnimationTick);
	}

	private void OnAnimationTick(TimeSpan _)
	{
		_animationFramePending = false;
		// detach 後の遅延コールバックは safely no-op
		if (_customVisual is null)
			return;

		if (NavigateAnimation is not null)
		{
			(Zoom, CenterLocation) = NavigateAnimation.GetCurrentParameter(Zoom, PaddedRect);
			if (!IsNavigating)
				NavigateAnimation = null;
		}

		ProcessInertiaFrame();

		ApplySize();
		RequestRedraw();

		var stillAnimating =
			IsNavigating
			|| (_inertiaAnimation?.IsRunning ?? false)
			|| _wheelZoomTracker.IsRunning
			|| (!IsHeadlessMode && Volatile.Read(ref _needsContinuousFrame) != 0);

		if (stillAnimating)
			RequestAnimationFrameIfNeeded();
	}

	private void ApplySize()
	{
		// DP Cache
		var renderSize = Bounds;
		PaddedRect = new RectD(new PointD(Padding.Left, Padding.Top), new PointD(Math.Max(0, renderSize.Width - Padding.Right), Math.Max(0, renderSize.Height - Padding.Bottom)));

		var halfRenderSize = new PointD(PaddedRect.Width / 2, PaddedRect.Height / 2);
		// 左上/右下のピクセル座標
		var leftTop = CenterLocation.ToPixel(Zoom) - halfRenderSize - new PointD(Padding.Left, Padding.Top);
		var rightBottom = CenterLocation.ToPixel(Zoom) + halfRenderSize + new PointD(Padding.Right, Padding.Bottom);

		var leftTopLocation = leftTop.ToLocation(Zoom).CastPoint();

		RenderParameter = new()
		{
			LeftTopLocation = leftTopLocation,
			LeftTopPixel = leftTop,
			PixelBound = new RectD(leftTop, rightBottom),
			ViewAreaRect = new RectD(leftTopLocation, rightBottom.ToLocation(Zoom).CastPoint()),
			Padding = Padding,
			Zoom = Zoom,
		};
	}

	private sealed class MapVisualHandler : CompositionCustomVisualHandler
	{
		public static readonly object RedrawMessage = new();

		private readonly MapControl _owner;

		public MapVisualHandler(MapControl owner) => _owner = owner;

		public override void OnMessage(object message)
		{
			if (message == RedrawMessage)
				Invalidate();
		}

		public override void OnRender(ImmediateDrawingContext drawingContext)
		{
			// この描画以降に届いた要求は次のフレームとして予約できる
			Volatile.Write(ref _owner._compositorRedrawPending, 0);
			if (!drawingContext.TryGetFeature<ISkiaSharpApiLeaseFeature>(out var leaseFeature))
				return;
			if (_owner.Layers is null)
				return;

			using var lease = leaseFeature.Lease();
			var canvas = lease.SkCanvas;

			LayerRenderParameter param;
			bool isAnim;
			lock (_owner._snapshotLock)
			{
				param = _owner._renderParamSnapshot;
				isAnim = _owner._isAnimatingSnapshot;
			}

			var shouldRecordMetrics = _owner.IsMetricsEnabled && DateTime.Now - _owner._lastMetricsRecordTime >= MetricsRecordInterval;
			var frameStopwatch = shouldRecordMetrics ? Stopwatch.StartNew() : null;

			var collectFrameStatistics = _owner.IsFrameStatisticsEnabled;
			var frameStartTimestamp = collectFrameStatistics ? Stopwatch.GetTimestamp() : 0L;

			bool needUpdate;
			canvas.Save();
			try
			{
				lock (_owner.LayerHost)
				{
					if (shouldRecordMetrics)
					{
						needUpdate = _owner.LayerHost.RenderWithMetrics(canvas, param, isAnim, out var layerMetrics);
						frameStopwatch!.Stop();

						var capturedTotal = frameStopwatch.Elapsed;
						var capturedMetrics = layerMetrics;
						var capturedParam = param;
						var capturedIsAnim = isAnim;
						Dispatcher.UIThread.Post(() =>
							_owner.LatestMetrics = new FrameRenderMetrics
							{
								TotalFrameTime = capturedTotal,
								LayerMetrics = capturedMetrics,
								Timestamp = DateTime.Now,
								IsNavigating = capturedIsAnim,
								Zoom = capturedParam.Zoom,
								LeftTopLocation = capturedParam.LeftTopLocation,
								LeftTopPixel = capturedParam.LeftTopPixel,
								PixelBound = capturedParam.PixelBound,
								ViewAreaRect = capturedParam.ViewAreaRect
							}
						);

						_owner._lastMetricsRecordTime = DateTime.Now;
					}
					else
					{
						needUpdate = _owner.LayerHost.Render(canvas, param, isAnim);
					}
				}
			}
			finally
			{
				canvas.Restore();
			}

			if (collectFrameStatistics)
				_owner.AccumulateFrameStatistics(frameStartTimestamp, Stopwatch.GetTimestamp());

			Volatile.Write(ref _owner._needsContinuousFrame, needUpdate ? 1 : 0);
			if (needUpdate && !_owner.IsHeadlessMode)
				Dispatcher.UIThread.Post(_owner.RequestAnimationFrameIfNeeded, DispatcherPriority.Background);
		}
	}
}
