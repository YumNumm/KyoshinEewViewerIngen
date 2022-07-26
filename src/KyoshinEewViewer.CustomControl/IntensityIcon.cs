using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using KyoshinMonitorLib;
using System;

namespace KyoshinEewViewer.CustomControl;

public class IntensityIcon : Control
{
	private JmaIntensity? intensity;
	public static readonly DirectProperty<IntensityIcon, JmaIntensity?> IntensityProperty =
		AvaloniaProperty.RegisterDirect<IntensityIcon, JmaIntensity?>(
			nameof(Intensity),
			o => o.intensity,
			(o, v) =>
			{
				o.intensity = v;
				o.InvalidateVisual();
			}
		);
	public JmaIntensity? Intensity
	{
		get => intensity;
		set => SetAndRaise(IntensityProperty, ref intensity, value);
	}

	private bool circleMode;
	public static readonly DirectProperty<IntensityIcon, bool> CircleModeProperty =
		AvaloniaProperty.RegisterDirect<IntensityIcon, bool>(
			nameof(CircleMode),
			o => o.circleMode,
			(o, v) =>
			{
				o.circleMode = v;
				o.InvalidateVisual();
			});
	public bool CircleMode
	{
		get => circleMode;
		set => SetAndRaise(CircleModeProperty, ref circleMode, value);
	}

	private bool wideMode;
	public static readonly DirectProperty<IntensityIcon, bool> WideModeProperty =
		AvaloniaProperty.RegisterDirect<IntensityIcon, bool>(
			nameof(WideMode),
			o => o.wideMode,
			(o, v) =>
			{
				o.wideMode = v;
				o.InvalidateMeasure();
				o.InvalidateVisual();
			});
	public bool WideMode
	{
		get => wideMode;
		set => SetAndRaise(WideModeProperty, ref wideMode, value);
	}

	private bool cornerRound;
	public static readonly DirectProperty<IntensityIcon, bool> CornerRoundProperty =
		AvaloniaProperty.RegisterDirect<IntensityIcon, bool>(
			nameof(CornerRound),
			o => o.cornerRound,
			(o, v) =>
			{
				o.cornerRound = v;
				o.InvalidateMeasure();
				o.InvalidateVisual();
			});
	public bool CornerRound
	{
		get => cornerRound;
		set => SetAndRaise(CornerRoundProperty, ref cornerRound, value);
	}

	protected override void OnInitialized()
	{
		base.OnInitialized();

		if (!FixedObjectRenderer.PaintCacheInitalized)
			FixedObjectRenderer.UpdateIntensityPaintCache(this);
	}

	public override void Render(DrawingContext context)
	{
		var size = Math.Min(DesiredSize.Width, DesiredSize.Height);
		context.DrawIntensity(Intensity ?? JmaIntensity.Error, new Point(), size, circle: CircleMode, wide: WideMode, round: CornerRound);
	}

	protected override Size MeasureOverride(Size availableSize)
	{
		var w = availableSize.Width;
		var h = availableSize.Height;

		if (h > w)
			return new Size(w, WideMode ? w * FixedObjectRenderer.INTENSITY_WIDE_SCALE : w);
		return new Size(WideMode ? h / FixedObjectRenderer.INTENSITY_WIDE_SCALE : h, h);
	}
}
