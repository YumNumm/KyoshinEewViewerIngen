using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using KyoshinEewViewer.Core;
using KyoshinEewViewer.Core.Models.Metrics;
using SkiaSharp;
using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;

namespace KyoshinEewViewer.Controls;

/// <summary>
/// Apple の Metal HUD 風に、地図の描画性能とメモリ使用量を半透明のパネルへ重ねて表示する
/// </summary>
public class PerformanceHudControl : Control
{
	public static readonly StyledProperty<FrameStatistics?> StatisticsProperty =
		AvaloniaProperty.Register<PerformanceHudControl, FrameStatistics?>(nameof(Statistics));

	/// <summary>
	/// 表示するフレーム統計
	/// </summary>
	public FrameStatistics? Statistics
	{
		get => GetValue(StatisticsProperty);
		set => SetValue(StatisticsProperty, value);
	}

	static PerformanceHudControl()
		=> AffectsRender<PerformanceHudControl>(StatisticsProperty);

	protected override Size MeasureOverride(Size availableSize)
		=> new(HudRenderOperation.PanelWidth, HudRenderOperation.GetPanelHeight());

	public override void Render(DrawingContext context)
	{
		base.Render(context);

		if (Bounds.Width <= 0 || Bounds.Height <= 0)
			return;

		// メモリの読み出しは描画スレッドへ渡せないため UI スレッドのここで済ませる
		context.Custom(new HudRenderOperation(
			new Rect(0, 0, Bounds.Width, Bounds.Height),
			Statistics,
			MemoryUsage.Capture()));
	}

	/// <summary>
	/// プロセスのメモリ使用量
	/// </summary>
	private readonly record struct MemoryUsage(long ManagedBytes, long? WorkingSetBytes, int Gen0, int Gen1, int Gen2)
	{
		public static MemoryUsage Capture() => new(
			GC.GetTotalMemory(false),
			TryGetWorkingSet(),
			GC.CollectionCount(0),
			GC.CollectionCount(1),
			GC.CollectionCount(2));
	}

	/// <summary>
	/// iOS など Process 系 API が制限される環境では取得できないため null を返す
	/// </summary>
	private static long? TryGetWorkingSet()
	{
		try
		{
			var workingSet = Environment.WorkingSet;
			return workingSet > 0 ? workingSet : null;
		}
		catch
		{
			return null;
		}
	}

	private sealed class HudRenderOperation : ICustomDrawOperation
	{
		public const float PanelWidth = 220;

		private const float PanelPadding = 8;
		private const float TitleHeight = 16;
		private const float RowHeight = 15;
		private const float RowBaseline = 11;
		private const float GraphHeight = 44;
		private const float SeparatorGap = 6;
		private const float GraphGap = 6;

		// 60fps / 30fps の目安。これを基準に色分けとグラフの縦スケールを決める
		private const float TargetFrameTimeMs = 1000f / 60;
		private const float SecondaryFrameTimeMs = 1000f / 30;

		private static readonly SKColor PanelColor = new(0, 0, 0, 190);
		private static readonly SKColor BorderColor = new(255, 255, 255, 40);
		private static readonly SKColor LabelColor = new(190, 190, 190);
		private static readonly SKColor ValueColor = new(255, 255, 255);
		private static readonly SKColor GraphBackgroundColor = new(255, 255, 255, 20);
		private static readonly SKColor GoodColor = new(110, 230, 130);
		private static readonly SKColor WarnColor = new(240, 220, 110);
		private static readonly SKColor BadColor = new(240, 120, 120);

		private static readonly SKFont TitleFont = new(KyoshinEewViewerFonts.MainBold, 11)
		{
			Edging = SKFontEdging.SubpixelAntialias,
			Subpixel = true,
		};
		private static readonly SKFont LabelFont = new(KyoshinEewViewerFonts.MainRegular, 11)
		{
			Edging = SKFontEdging.SubpixelAntialias,
			Subpixel = true,
		};
		private static readonly SKFont CaptionFont = new(KyoshinEewViewerFonts.MainRegular, 9)
		{
			Edging = SKFontEdging.SubpixelAntialias,
			Subpixel = true,
		};
		private static readonly SKFont ValueFont = new(ResolveMonospaceTypeface(), 11)
		{
			Edging = SKFontEdging.SubpixelAntialias,
			Subpixel = true,
		};

		/// <summary>
		/// 桁が揺れないように等幅フォントを使う。アプリに同梱していないため OS ごとの候補を順に探す
		/// </summary>
		private static SKTypeface ResolveMonospaceTypeface()
		{
			string[] candidates = ["Consolas", "Menlo", "DejaVu Sans Mono", "Liberation Mono", "Courier New", "monospace"];
			var families = SKFontManager.Default.GetFontFamilies();
			foreach (var candidate in candidates)
			{
				if (!families.Contains(candidate, StringComparer.OrdinalIgnoreCase))
					continue;
				if (SKTypeface.FromFamilyName(candidate) is { } typeface)
					return typeface;
			}
			return SKTypeface.Default;
		}

		private static readonly string PlatformName = BuildPlatformName();

		private static string BuildPlatformName()
		{
			var os = true switch
			{
				_ when OperatingSystem.IsMacCatalyst() => "Mac Catalyst",
				_ when OperatingSystem.IsIOS() => "iOS",
				_ when OperatingSystem.IsMacOS() => "macOS",
				_ when OperatingSystem.IsAndroid() => "Android",
				_ when OperatingSystem.IsWindows() => "Windows",
				_ when OperatingSystem.IsLinux() => "Linux",
				_ => "Unknown",
			};
			return $"{os} {RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";
		}

		/// <summary>
		/// 行数はプロセスメモリを取得できるかどうかで変わる
		/// </summary>
		private static readonly int RowCount = TryGetWorkingSet() is null ? 9 : 10;

		public static float GetPanelHeight()
			=> PanelPadding + TitleHeight + SeparatorGap + RowHeight * RowCount + GraphGap + GraphHeight + PanelPadding;

		public Rect Bounds { get; }
		private FrameStatistics? Statistics { get; }
		private MemoryUsage Memory { get; }

		public HudRenderOperation(Rect bounds, FrameStatistics? statistics, MemoryUsage memory)
		{
			Bounds = bounds;
			Statistics = statistics;
			Memory = memory;
		}

		public void Dispose() => GC.SuppressFinalize(this);
		public bool Equals(ICustomDrawOperation? other) => false;
		public bool HitTest(Point p) => false;

		public void Render(ImmediateDrawingContext context)
		{
			if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>(out var leaseFeature) is false)
				return;
			using var lease = leaseFeature.Lease();
			RenderHud(lease.SkCanvas, (float)Bounds.Width, (float)Bounds.Height);
		}

		private void RenderHud(SKCanvas canvas, float width, float height)
		{
			using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
			using var strokePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };

			fillPaint.Color = PanelColor;
			canvas.DrawRoundRect(new SKRoundRect(new SKRect(0, 0, width, height), 6), fillPaint);
			strokePaint.Color = BorderColor;
			canvas.DrawRoundRect(new SKRoundRect(new SKRect(.5f, .5f, width - .5f, height - .5f), 6), strokePaint);

			fillPaint.Color = ValueColor;
			canvas.DrawText("Performance HUD", new SKPoint(PanelPadding, PanelPadding + 11), SKTextAlign.Left, TitleFont, fillPaint);
			fillPaint.Color = LabelColor;
			canvas.DrawText(PlatformName, new SKPoint(width - PanelPadding, PanelPadding + 11), SKTextAlign.Right, CaptionFont, fillPaint);

			var y = PanelPadding + TitleHeight;
			strokePaint.Color = BorderColor;
			canvas.DrawLine(PanelPadding, y + .5f, width - PanelPadding, y + .5f, strokePaint);
			y += SeparatorGap;

			var stats = Statistics;
			y = DrawRow(canvas, fillPaint, width, y, "解像度",
				stats is null ? "-" : $"{stats.RenderWidth}x{stats.RenderHeight} @{stats.RenderScaling.ToString("0.#", CultureInfo.InvariantCulture)}x");
			y = DrawRow(canvas, fillPaint, width, y, "FPS",
				stats is null ? "-" : stats.Fps.ToString("F1", CultureInfo.InvariantCulture));
			y = DrawRow(canvas, fillPaint, width, y, "描画時間",
				stats is null ? "-" : $"{stats.AverageRenderTimeMs.ToString("F2", CultureInfo.InvariantCulture)}ms",
				stats is null ? ValueColor : GetFrameTimeColor((float)stats.AverageRenderTimeMs));
			y = DrawRow(canvas, fillPaint, width, y, "最大描画時間",
				stats is null ? "-" : $"{stats.MaxRenderTimeMs.ToString("F2", CultureInfo.InvariantCulture)}ms");
			y = DrawRow(canvas, fillPaint, width, y, "フレーム間隔",
				stats is null ? "-" : $"{stats.AverageIntervalMs.ToString("F2", CultureInfo.InvariantCulture)}ms");
			// 等幅フォントは日本語の字形を持たないため、日本語の値はラベルと同じフォントで描く
			y = DrawRow(canvas, fillPaint, width, y, "描画要求",
				stats is null ? "-" : stats.IsContinuousRendering ? "連続" : "停止",
				valueFont: LabelFont);
			y = DrawRow(canvas, fillPaint, width, y, "レイヤー / ズーム",
				stats is null ? "-" : $"{stats.LayerCount} / {stats.Zoom.ToString("F2", CultureInfo.InvariantCulture)}");
			y = DrawRow(canvas, fillPaint, width, y, "マネージド", FormatBytes(Memory.ManagedBytes));
			if (Memory.WorkingSetBytes is { } workingSet)
				y = DrawRow(canvas, fillPaint, width, y, "プロセス", FormatBytes(workingSet));
			y = DrawRow(canvas, fillPaint, width, y, "GC 0/1/2", $"{Memory.Gen0}/{Memory.Gen1}/{Memory.Gen2}");

			DrawGraph(canvas, fillPaint, strokePaint, PanelPadding, y + GraphGap, width - PanelPadding * 2, GraphHeight);
		}

		private static float DrawRow(SKCanvas canvas, SKPaint paint, float width, float y, string label, string value, SKColor? valueColor = null, SKFont? valueFont = null)
		{
			paint.Color = LabelColor;
			canvas.DrawText(label, new SKPoint(PanelPadding, y + RowBaseline), SKTextAlign.Left, LabelFont, paint);
			paint.Color = valueColor ?? ValueColor;
			canvas.DrawText(value, new SKPoint(width - PanelPadding, y + RowBaseline), SKTextAlign.Right, valueFont ?? ValueFont, paint);
			return y + RowHeight;
		}

		private void DrawGraph(SKCanvas canvas, SKPaint fillPaint, SKPaint strokePaint, float left, float top, float width, float height)
		{
			fillPaint.Color = GraphBackgroundColor;
			canvas.DrawRect(left, top, width, height, fillPaint);

			var history = Statistics?.RenderTimeHistoryMs;
			var scaleMax = SecondaryFrameTimeMs;
			if (history is { Length: > 0 })
				// 起動直後などの突出したスパイクに合わせると通常フレームが潰れて読めなくなるため上限を設け、
				// 超えた分はグラフ上端で切る
				scaleMax = Math.Clamp(history.Max() * 1.1f, SecondaryFrameTimeMs, SecondaryFrameTimeMs * 2);

			// 60fps の目安線
			strokePaint.Color = GoodColor.WithAlpha(90);
			var targetY = top + height - height * TargetFrameTimeMs / scaleMax;
			canvas.DrawLine(left, targetY, left + width, targetY, strokePaint);

			fillPaint.Color = LabelColor;
			canvas.DrawText($"描画時間 0〜{scaleMax.ToString("F0", CultureInfo.InvariantCulture)}ms",
				new SKPoint(left + 3, top + 10), SKTextAlign.Left, CaptionFont, fillPaint);

			if (history is not { Length: > 0 })
				return;

			// 履歴の長さに関わらず1サンプルの幅を固定し、新しいものを右端へ寄せる
			var slotWidth = width / FrameTimeHistorySlots;
			var barWidth = Math.Max(slotWidth - .5f, .8f);
			for (var i = 0; i < history.Length && i < FrameTimeHistorySlots; i++)
			{
				var value = history[history.Length - 1 - i];
				var barHeight = Math.Min(height, height * value / scaleMax);
				var x = left + width - (i + 1) * slotWidth;
				fillPaint.Color = GetFrameTimeColor(value);
				canvas.DrawRect(x, top + height - barHeight, barWidth, barHeight, fillPaint);
			}
		}

		/// <summary>
		/// MapControl が保持する履歴の長さと合わせておく
		/// </summary>
		private const int FrameTimeHistorySlots = 120;

		private static SKColor GetFrameTimeColor(float frameTimeMs) => frameTimeMs switch
		{
			<= TargetFrameTimeMs => GoodColor,
			<= SecondaryFrameTimeMs => WarnColor,
			_ => BadColor,
		};

		private static string FormatBytes(long bytes)
			=> bytes >= 1024L * 1024 * 1024
				? $"{(bytes / 1024.0 / 1024 / 1024).ToString("F2", CultureInfo.InvariantCulture)}GB"
				: $"{(bytes / 1024.0 / 1024).ToString("F1", CultureInfo.InvariantCulture)}MB";
	}
}
