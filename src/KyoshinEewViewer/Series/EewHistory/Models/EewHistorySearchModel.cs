using KyoshinEewViewer.Services.EqMonitor;
using ReactiveUI;
using System;
using System.Collections.Generic;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Series.EewHistory.Models;

/// <summary>
/// EEW履歴の検索条件を入力するためのモデル
/// </summary>
public class EewHistorySearchModel : ReactiveObject
{
	/// <summary>
	/// 震度の選択肢<br/>
	/// 「5弱以上」のような推定値は範囲指定に向かないため除く
	/// </summary>
	public static IReadOnlyList<IntensityChoice> IntensityChoices { get; } =
	[
		new("指定なし", null),
		new("1", Generated.JmaIntensity._1),
		new("2", Generated.JmaIntensity._2),
		new("3", Generated.JmaIntensity._3),
		new("4", Generated.JmaIntensity._4),
		new("5弱", Generated.JmaIntensity._5Minus),
		new("5強", Generated.JmaIntensity._5Plus),
		new("6弱", Generated.JmaIntensity._6Minus),
		new("6強", Generated.JmaIntensity._6Plus),
		new("7", Generated.JmaIntensity._7),
	];

	public static IReadOnlyList<StatusChoice> StatusChoices { get; } =
	[
		new("通常", null),
		new("訓練", Generated.TelegramStatus.TRAINING),
		new("試験", Generated.TelegramStatus.TEST),
	];

	public static IReadOnlyList<WarningFilterChoice> WarningFilterChoices { get; } =
	[
		new("指定なし", null),
		new("警報のみ", true),
		new("警報以外", false),
	];

	public record IntensityChoice(string Name, Generated.JmaIntensity? Value);
	public record StatusChoice(string Name, Generated.TelegramStatus? Value);
	public record WarningFilterChoice(string Name, bool? Value);

	private DateTimeOffset? _originTimeFrom;
	public DateTimeOffset? OriginTimeFrom
	{
		get => _originTimeFrom;
		set => this.RaiseAndSetIfChanged(ref _originTimeFrom, value);
	}

	private DateTimeOffset? _originTimeTo;
	public DateTimeOffset? OriginTimeTo
	{
		get => _originTimeTo;
		set => this.RaiseAndSetIfChanged(ref _originTimeTo, value);
	}

	private double? _magnitudeFrom;
	public double? MagnitudeFrom
	{
		get => _magnitudeFrom;
		set => this.RaiseAndSetIfChanged(ref _magnitudeFrom, value);
	}

	private double? _magnitudeTo;
	public double? MagnitudeTo
	{
		get => _magnitudeTo;
		set => this.RaiseAndSetIfChanged(ref _magnitudeTo, value);
	}

	private double? _depthFrom;
	public double? DepthFrom
	{
		get => _depthFrom;
		set => this.RaiseAndSetIfChanged(ref _depthFrom, value);
	}

	private double? _depthTo;
	public double? DepthTo
	{
		get => _depthTo;
		set => this.RaiseAndSetIfChanged(ref _depthTo, value);
	}

	private IntensityChoice _intensityFrom = IntensityChoices[0];
	public IntensityChoice IntensityFrom
	{
		get => _intensityFrom;
		set => this.RaiseAndSetIfChanged(ref _intensityFrom, value);
	}

	private IntensityChoice _intensityTo = IntensityChoices[0];
	public IntensityChoice IntensityTo
	{
		get => _intensityTo;
		set => this.RaiseAndSetIfChanged(ref _intensityTo, value);
	}

	private StatusChoice _status = StatusChoices[0];
	public StatusChoice Status
	{
		get => _status;
		set => this.RaiseAndSetIfChanged(ref _status, value);
	}

	private WarningFilterChoice _warningFilter = WarningFilterChoices[0];
	/// <summary>
	/// 警報による絞り込み
	/// </summary>
	public WarningFilterChoice WarningFilter
	{
		get => _warningFilter;
		set => this.RaiseAndSetIfChanged(ref _warningFilter, value);
	}

	private string? _error;
	/// <summary>
	/// 直近の検索が失敗した理由
	/// </summary>
	public string? Error
	{
		get => _error;
		set => this.RaiseAndSetIfChanged(ref _error, value);
	}

	private bool _isSearching;
	/// <summary>
	/// 検索の実行中か
	/// </summary>
	public bool IsSearching
	{
		get => _isSearching;
		set => this.RaiseAndSetIfChanged(ref _isSearching, value);
	}

	/// <summary>
	/// 入力内容から検索条件を組み立てる
	/// </summary>
	public EqMonitorEewSearchCondition ToCondition()
		=> new()
		{
			OriginTimeFrom = OriginTimeFrom,
			OriginTimeTo = OriginTimeTo,
			MagnitudeFrom = MagnitudeFrom,
			MagnitudeTo = MagnitudeTo,
			DepthFrom = DepthFrom,
			DepthTo = DepthTo,
			IntensityFrom = IntensityFrom.Value,
			IntensityTo = IntensityTo.Value,
			Status = Status.Value,
			IsWarning = WarningFilter.Value,
		};

	/// <summary>
	/// 入力内容を初期状態へ戻す
	/// </summary>
	public void Reset()
	{
		OriginTimeFrom = OriginTimeTo = null;
		MagnitudeFrom = MagnitudeTo = null;
		DepthFrom = DepthTo = null;
		IntensityFrom = IntensityTo = IntensityChoices[0];
		Status = StatusChoices[0];
		WarningFilter = WarningFilterChoices[0];
		Error = null;
	}
}
