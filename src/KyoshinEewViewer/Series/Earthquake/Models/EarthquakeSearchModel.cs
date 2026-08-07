using KyoshinEewViewer.Services.EqMonitor;
using ReactiveUI;
using System;
using System.Collections.Generic;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Series.Earthquake.Models;

/// <summary>
/// 地震履歴の検索条件を入力するためのモデル
/// </summary>
public class EarthquakeSearchModel : ReactiveObject
{
	/// <summary>
	/// 震度の選択肢<br/>
	/// 「5弱以上」のような震度速報の推定値は範囲指定に向かないため除く
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

	public static IReadOnlyList<LpgmIntensityChoice> LpgmIntensityChoices { get; } =
	[
		new("指定なし", null),
		new("1", Generated.JmaLpgmIntensity._1),
		new("2", Generated.JmaLpgmIntensity._2),
		new("3", Generated.JmaLpgmIntensity._3),
		new("4", Generated.JmaLpgmIntensity._4),
	];

	public static IReadOnlyList<EarthquakeTypeChoice> EarthquakeTypeChoices { get; } =
	[
		new("指定なし", null),
		new("通常の地震", Generated.EarthquakeType.NORMAL),
		new("遠地地震", Generated.EarthquakeType.DISTANT),
		new("大規模噴火", Generated.EarthquakeType.VOLCANO),
	];

	public static IReadOnlyList<StatusChoice> StatusChoices { get; } =
	[
		new("通常", null),
		new("訓練", Generated.TelegramStatus.TRAINING),
		new("試験", Generated.TelegramStatus.TEST),
	];

	public static IReadOnlyList<DatasourceChoice> DatasourceChoices { get; } =
	[
		new("指定なし", null),
		new("気象庁震度データベース", Generated.EarthquakeDatasource.JMA_INTENSITY_DATABASE),
		new("防災情報XML", Generated.EarthquakeDatasource.JMA_DISASTER_INFORMATION_XML),
	];

	public static IReadOnlyList<TelegramTypeChoice> TelegramTypeChoices { get; } =
	[
		new("指定なし", null),
		new("震度速報", Generated.EarthquakeTelegramType.VXSE51),
		new("震源に関する情報", Generated.EarthquakeTelegramType.VXSE52),
		new("震源・震度に関する情報", Generated.EarthquakeTelegramType.VXSE53),
		new("長周期地震動に関する観測情報", Generated.EarthquakeTelegramType.VXSE62),
	];

	public static IReadOnlyList<SortByChoice> SortByChoices { get; } =
	[
		new("発生時刻", Generated.EarthquakeSortBy.Event_id),
		new("マグニチュード", Generated.EarthquakeSortBy.Magnitude),
		new("最大震度", Generated.EarthquakeSortBy.Max_intensity),
		new("深さ", Generated.EarthquakeSortBy.Depth),
	];

	public static IReadOnlyList<SortOrderChoice> SortOrderChoices { get; } =
	[
		new("降順", Generated.SortOrder.DESC),
		new("昇順", Generated.SortOrder.ASC),
	];

	public record IntensityChoice(string Name, Generated.JmaIntensity? Value);
	public record LpgmIntensityChoice(string Name, Generated.JmaLpgmIntensity? Value);
	public record EarthquakeTypeChoice(string Name, Generated.EarthquakeType? Value);
	public record StatusChoice(string Name, Generated.TelegramStatus? Value);
	public record DatasourceChoice(string Name, Generated.EarthquakeDatasource? Value);
	public record TelegramTypeChoice(string Name, Generated.EarthquakeTelegramType? Value);
	public record SortByChoice(string Name, Generated.EarthquakeSortBy Value);
	public record SortOrderChoice(string Name, Generated.SortOrder Value);

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

	private LpgmIntensityChoice _lpgmIntensityFrom = LpgmIntensityChoices[0];
	public LpgmIntensityChoice LpgmIntensityFrom
	{
		get => _lpgmIntensityFrom;
		set => this.RaiseAndSetIfChanged(ref _lpgmIntensityFrom, value);
	}

	private LpgmIntensityChoice _lpgmIntensityTo = LpgmIntensityChoices[0];
	public LpgmIntensityChoice LpgmIntensityTo
	{
		get => _lpgmIntensityTo;
		set => this.RaiseAndSetIfChanged(ref _lpgmIntensityTo, value);
	}

	private double? _latitudeFrom;
	public double? LatitudeFrom
	{
		get => _latitudeFrom;
		set => this.RaiseAndSetIfChanged(ref _latitudeFrom, value);
	}

	private double? _latitudeTo;
	public double? LatitudeTo
	{
		get => _latitudeTo;
		set => this.RaiseAndSetIfChanged(ref _latitudeTo, value);
	}

	private double? _longitudeFrom;
	public double? LongitudeFrom
	{
		get => _longitudeFrom;
		set => this.RaiseAndSetIfChanged(ref _longitudeFrom, value);
	}

	private double? _longitudeTo;
	public double? LongitudeTo
	{
		get => _longitudeTo;
		set => this.RaiseAndSetIfChanged(ref _longitudeTo, value);
	}

	private EarthquakeTypeChoice _earthquakeType = EarthquakeTypeChoices[0];
	public EarthquakeTypeChoice EarthquakeType
	{
		get => _earthquakeType;
		set => this.RaiseAndSetIfChanged(ref _earthquakeType, value);
	}

	private StatusChoice _status = StatusChoices[0];
	public StatusChoice Status
	{
		get => _status;
		set => this.RaiseAndSetIfChanged(ref _status, value);
	}

	private DatasourceChoice _datasource = DatasourceChoices[0];
	public DatasourceChoice Datasource
	{
		get => _datasource;
		set => this.RaiseAndSetIfChanged(ref _datasource, value);
	}

	private TelegramTypeChoice _telegramType = TelegramTypeChoices[0];
	public TelegramTypeChoice TelegramType
	{
		get => _telegramType;
		set => this.RaiseAndSetIfChanged(ref _telegramType, value);
	}

	private SortByChoice _sortBy = SortByChoices[0];
	public SortByChoice SortBy
	{
		get => _sortBy;
		set {
			this.RaiseAndSetIfChanged(ref _sortBy, value);
			this.RaisePropertyChanged(nameof(IsPagingUnavailable));
		}
	}

	private SortOrderChoice _sortOrder = SortOrderChoices[0];
	public SortOrderChoice SortOrder
	{
		get => _sortOrder;
		set => this.RaiseAndSetIfChanged(ref _sortOrder, value);
	}

	/// <summary>
	/// 現在の並び順ではページングを行えないか
	/// </summary>
	public bool IsPagingUnavailable => SortBy.Value != Generated.EarthquakeSortBy.Event_id;

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
	public EqMonitorSearchCondition ToCondition()
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
			LpgmIntensityFrom = LpgmIntensityFrom.Value,
			LpgmIntensityTo = LpgmIntensityTo.Value,
			LatitudeFrom = LatitudeFrom,
			LatitudeTo = LatitudeTo,
			LongitudeFrom = LongitudeFrom,
			LongitudeTo = LongitudeTo,
			EarthquakeType = EarthquakeType.Value,
			Status = Status.Value,
			Datasource = Datasource.Value,
			TelegramType = TelegramType.Value,
			SortBy = SortBy.Value,
			SortOrder = SortOrder.Value,
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
		LpgmIntensityFrom = LpgmIntensityTo = LpgmIntensityChoices[0];
		LatitudeFrom = LatitudeTo = LongitudeFrom = LongitudeTo = null;
		EarthquakeType = EarthquakeTypeChoices[0];
		Status = StatusChoices[0];
		Datasource = DatasourceChoices[0];
		TelegramType = TelegramTypeChoices[0];
		SortBy = SortByChoices[0];
		SortOrder = SortOrderChoices[0];
		Error = null;
	}

	/// <summary>
	/// 地図で選択した矩形を反映する
	/// </summary>
	public void ApplyEpicenterRect(double latitude1, double longitude1, double latitude2, double longitude2)
	{
		LatitudeFrom = Math.Min(latitude1, latitude2);
		LatitudeTo = Math.Max(latitude1, latitude2);
		LongitudeFrom = Math.Min(longitude1, longitude2);
		LongitudeTo = Math.Max(longitude1, longitude2);
	}
}
