using Avalonia.Controls;
using Avalonia.Platform.Storage;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Core.Models.Events;
using KyoshinEewViewer.Core.Models.Metrics;
using KyoshinEewViewer.Services;
using KyoshinEewViewer.Services.NetworkDebug;
using KyoshinEewViewer.Services.TelegramPublishers.Dmdata;
using ReactiveUI;
using Splat;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace KyoshinEewViewer.ViewModels;

public class DebugWindowViewModel : ViewModelBase, IDisposable
{
	public string Title => "デバッグウィンドウ";

	public KyoshinEewViewerConfiguration Config { get; }

	private DmdataRedundantTelegramPublisher? DmdataPublisher { get; }

	/// <summary>DM-D.S.S WebSocket の Pong 送信までの遅延（ms）。直接接続時のみ。</summary>
	public long? DmdataLastPongSendMs => DmdataPublisher?.LastPongSendMilliseconds;

	/// <summary>前回 ping からの間隔（秒）。</summary>
	public double? DmdataLastPingIntervalSec => DmdataPublisher?.LastPingIntervalSeconds;

	private PropertyChangedEventHandler? _dmdataPublisherPropertyChanged;

	private IDisposable? _metricsSubscription;
	private IDisposable? _logSubscription;
	private IDisposable? _networkSubscription;
	private bool _isActive;
	private readonly InMemoryLoggerProvider? _loggerProvider;
	private readonly NetworkDebugRecorder? _networkRecorder;

	/// <summary>
	/// 絞り込み前のすべての通信記録
	/// </summary>
	private readonly List<NetworkTransactionViewModel> _allNetworkTransactions = [];

	private ObservableCollection<LayerMetricsViewModel> _layerMetrics = [];
	public ObservableCollection<LayerMetricsViewModel> LayerMetrics
	{
		get => _layerMetrics;
		set => this.RaiseAndSetIfChanged(ref _layerMetrics, value);
	}

	private string _totalFrameTime = "-";
	public string TotalFrameTime
	{
		get => _totalFrameTime;
		set => this.RaiseAndSetIfChanged(ref _totalFrameTime, value);
	}

	private string _zoom = "-";
	public string Zoom
	{
		get => _zoom;
		set => this.RaiseAndSetIfChanged(ref _zoom, value);
	}

	private string _isNavigating = "-";
	public string IsNavigating
	{
		get => _isNavigating;
		set => this.RaiseAndSetIfChanged(ref _isNavigating, value);
	}

	private string _timestamp = "-";
	public string Timestamp
	{
		get => _timestamp;
		set => this.RaiseAndSetIfChanged(ref _timestamp, value);
	}

	private ObservableCollection<LogEntryViewModel> _logEntries = [];
	public ObservableCollection<LogEntryViewModel> LogEntries
	{
		get => _logEntries;
		set => this.RaiseAndSetIfChanged(ref _logEntries, value);
	}

	private bool _autoScroll = true;
	public bool AutoScroll
	{
		get => _autoScroll;
		set => this.RaiseAndSetIfChanged(ref _autoScroll, value);
	}

	private bool _scrollToEnd;
	public bool ScrollToEnd
	{
		get => _scrollToEnd;
		set => this.RaiseAndSetIfChanged(ref _scrollToEnd, value);
	}

	/// <summary>
	/// WebSocket接続先 URL（直接接続モード: ws:// または wss:// で始まる URL）
	/// </summary>
	public string? WebSocketOverrideUrl
	{
		get => Config.Dmdata.WebSocketDefaultEndpoint;
		set
		{
			Config.Dmdata.WebSocketDefaultEndpoint = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
			this.RaisePropertyChanged();
		}
	}

	/// <summary>
	/// 冗長接続エンドポイント（改行区切り）
	/// </summary>
	public string WebSocketRedundantEndpointsText
	{
		get => string.Join(Environment.NewLine, Config.Dmdata.WebSocketRedundantEndpoints ?? []);
		set
		{
			Config.Dmdata.WebSocketRedundantEndpoints = value
				.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Where(s => !string.IsNullOrWhiteSpace(s))
				.ToArray();
			if (Config.Dmdata.WebSocketRedundantEndpoints.Length == 0)
				Config.Dmdata.WebSocketRedundantEndpoints = null;
			this.RaisePropertyChanged();
		}
	}

	private ObservableCollection<NetworkTransactionViewModel> _networkTransactions = [];
	/// <summary>
	/// 絞り込み後の通信記録
	/// </summary>
	public ObservableCollection<NetworkTransactionViewModel> NetworkTransactions
	{
		get => _networkTransactions;
		set => this.RaiseAndSetIfChanged(ref _networkTransactions, value);
	}

	private NetworkTransactionViewModel? _selectedNetworkTransaction;
	public NetworkTransactionViewModel? SelectedNetworkTransaction
	{
		get => _selectedNetworkTransaction;
		set => this.RaiseAndSetIfChanged(ref _selectedNetworkTransaction, value);
	}

	/// <summary>
	/// 通信内容を記録するか
	/// </summary>
	public bool RecordNetworkTraffic
	{
		get => Config.Debug.RecordNetworkTraffic;
		set
		{
			Config.Debug.RecordNetworkTraffic = value;
			this.RaisePropertyChanged();
		}
	}

	private string _networkHostFilter = "";
	/// <summary>
	/// 接続先ホストによる絞り込み
	/// </summary>
	public string NetworkHostFilter
	{
		get => _networkHostFilter;
		set
		{
			this.RaiseAndSetIfChanged(ref _networkHostFilter, value);
			ApplyNetworkFilter();
		}
	}

	private bool _isDmdataReconnecting;
	public bool IsDmdataReconnecting
	{
		get => _isDmdataReconnecting;
		set => this.RaiseAndSetIfChanged(ref _isDmdataReconnecting, value);
	}

	private string? _dmdataReconnectStatus;
	public string? DmdataReconnectStatus
	{
		get => _dmdataReconnectStatus;
		set => this.RaiseAndSetIfChanged(ref _dmdataReconnectStatus, value);
	}

	public DebugWindowViewModel(KyoshinEewViewerConfiguration config, InMemoryLoggerProvider? loggerProvider = null)
	{
		SplatRegistrations.RegisterLazySingleton<DebugWindowViewModel>();

		Config = config;
		_loggerProvider = loggerProvider ?? Locator.Current.GetService<InMemoryLoggerProvider>();
		_networkRecorder = Locator.Current.GetService<NetworkDebugRecorder>();
		DmdataPublisher = Locator.Current.GetService<DmdataRedundantTelegramPublisher>();
		if (DmdataPublisher is INotifyPropertyChanged dmdataInpc)
		{
			_dmdataPublisherPropertyChanged = (_, e) =>
			{
				if (e.PropertyName is nameof(DmdataRedundantTelegramPublisher.LastPongSendMilliseconds)
				    or nameof(DmdataRedundantTelegramPublisher.LastPingIntervalSeconds))
				{
					this.RaisePropertyChanged(nameof(DmdataLastPongSendMs));
					this.RaisePropertyChanged(nameof(DmdataLastPingIntervalSec));
				}
			};
			dmdataInpc.PropertyChanged += _dmdataPublisherPropertyChanged;
		}

		// メトリクス更新イベントをサブスクライブ
		_metricsSubscription = MessageBus.Current.Listen<MetricsUpdated>()
			.ObserveOn(RxSchedulers.MainThreadScheduler)
			.Subscribe(msg => UpdateMetrics(msg.Metrics));

		// ログ追加イベントをサブスクライブ
		_logSubscription = MessageBus.Current.Listen<LogEntryAdded>()
			.ObserveOn(RxSchedulers.MainThreadScheduler)
			.Subscribe(msg => AddLogEntry(msg.Entry));

		// 通信記録は強震モニタにより毎秒発生するため、まとめて追加して描画負荷を抑える
		_networkSubscription = MessageBus.Current.Listen<NetworkTransactionAdded>()
			.Buffer(TimeSpan.FromMilliseconds(250))
			.Where(messages => messages.Count > 0)
			.ObserveOn(RxSchedulers.MainThreadScheduler)
			.Subscribe(AddNetworkTransactions);

		// 初回ログ読み込み
		LoadInitialLogs();
		LoadInitialNetworkTransactions();

		// ウィンドウがアクティブになったらメトリクス収集を有効化
		Activate();
	}

	/// <summary>
	/// 初回ログ読み込み
	/// </summary>
	private void LoadInitialLogs()
	{
		if (_loggerProvider == null)
			return;

		var logs = _loggerProvider.GetLogs();
		foreach (var log in logs)
		{
			AddLogEntry(log);
		}
	}

	/// <summary>
	/// 通信タブを開く前に発生していた記録を読み込む
	/// </summary>
	private void LoadInitialNetworkTransactions()
	{
		if (_networkRecorder == null)
			return;

		foreach (var transaction in _networkRecorder.GetAll())
			_allNetworkTransactions.Add(NetworkTransactionViewModel.From(transaction));
		ApplyNetworkFilter();
	}

	private void AddNetworkTransactions(IList<NetworkTransactionAdded> messages)
	{
		foreach (var message in messages)
		{
			var viewModel = NetworkTransactionViewModel.From(message.Transaction);
			_allNetworkTransactions.Add(viewModel);
			if (IsMatchNetworkFilter(viewModel))
				NetworkTransactions.Add(viewModel);
		}

		while (_allNetworkTransactions.Count > NetworkDebugRecorder.MaxEntryCount)
			_allNetworkTransactions.RemoveAt(0);
		while (NetworkTransactions.Count > NetworkDebugRecorder.MaxEntryCount)
			NetworkTransactions.RemoveAt(0);
	}

	private bool IsMatchNetworkFilter(NetworkTransactionViewModel viewModel)
		=> string.IsNullOrWhiteSpace(NetworkHostFilter) ||
			viewModel.Host.Contains(NetworkHostFilter.Trim(), StringComparison.OrdinalIgnoreCase);

	private void ApplyNetworkFilter()
	{
		NetworkTransactions.Clear();
		foreach (var viewModel in _allNetworkTransactions.Where(IsMatchNetworkFilter))
			NetworkTransactions.Add(viewModel);
	}

	/// <summary>
	/// 通信記録をクリア
	/// </summary>
	public void ClearNetworkTransactions()
	{
		_networkRecorder?.Clear();
		_allNetworkTransactions.Clear();
		NetworkTransactions.Clear();
		SelectedNetworkTransaction = null;
	}

	/// <summary>
	/// ログをクリア
	/// </summary>
	public void ClearLogs()
	{
		_loggerProvider?.Clear();
		LogEntries.Clear();
	}

	/// <summary>
	/// DMDATA WebSocket を再接続する（デバッグ用）
	/// </summary>
	public async Task ReconnectDmdataAsync()
	{
		if (DmdataPublisher == null)
		{
			DmdataReconnectStatus = "DmdataPublisher が利用できません";
			return;
		}

		IsDmdataReconnecting = true;
		DmdataReconnectStatus = "再接続中...";
		try
		{
			await DmdataPublisher.ForceReconnectAsync();
			DmdataReconnectStatus = "再接続要求を送信しました";
		}
		catch (Exception ex)
		{
			DmdataReconnectStatus = $"エラー: {ex.Message}";
		}
		finally
		{
			IsDmdataReconnecting = false;
		}
	}

	/// <summary>
	/// 地図画像を保存する
	/// </summary>
	public async Task SaveMapImageAsync()
	{
		if (KyoshinEewViewerApp.TopLevelControl is not Window window)
			return;

		var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = "地図画像を保存",
			DefaultExtension = "skp",
			SuggestedFileName = $"map_{DateTime.Now:yyyyMMdd_HHmmss}.skp",
			FileTypeChoices =
			[
				new FilePickerFileType("SKPicture") { Patterns = ["*.skp"] },
				new FilePickerFileType("PNG画像") { Patterns = ["*.png"] },
				new FilePickerFileType("JPEG画像") { Patterns = ["*.jpg", "*.jpeg"] },
				new FilePickerFileType("WEBP画像") { Patterns = ["*.webp"] }
			]
		});

		if (file is null)
			return;

		MessageBus.Current.SendMessage(new MapImageSaveRequested { TargetPath = file.Path.LocalPath });
	}

	/// <summary>
	/// メトリクス収集を有効化
	/// </summary>
	public void Activate()
	{
		if (_isActive) return;
		_isActive = true;
		MessageBus.Current.SendMessage(new MetricsEnabledChanged { IsEnabled = true });
	}

	/// <summary>
	/// メトリクス収集を無効化
	/// </summary>
	public void Deactivate()
	{
		if (!_isActive) return;
		_isActive = false;
		MessageBus.Current.SendMessage(new MetricsEnabledChanged { IsEnabled = false });
	}

	public void Dispose()
	{
		Deactivate();
		if (DmdataPublisher is INotifyPropertyChanged dmdataInpc && _dmdataPublisherPropertyChanged is { } h)
			dmdataInpc.PropertyChanged -= h;
		_metricsSubscription?.Dispose();
		_logSubscription?.Dispose();
		_networkSubscription?.Dispose();
		GC.SuppressFinalize(this);
	}

	/// <summary>
	/// ログエントリを追加
	/// </summary>
	private void AddLogEntry(LogEntry log)
	{
		LogEntries.Add(new LogEntryViewModel
		{
			Timestamp = log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
			LogLevel = log.LogLevel.ToString(),
			Category = GetShortCategoryName(log.CategoryName),
			Message = log.Message,
			Exception = log.Exception?.ToString()
		});

		// 最大1000件に制限
		while (LogEntries.Count > 1000)
			LogEntries.RemoveAt(0);

		// 自動スクロールが有効な場合、スクロールをトリガー
		if (AutoScroll)
			TriggerScrollToEnd();
	}

	/// <summary>
	/// スクロールを最下部にトリガー
	/// </summary>
	private void TriggerScrollToEnd()
	{
		// トグルしてバインディングをトリガー
		ScrollToEnd = false;
		ScrollToEnd = true;
	}

	private static string GetShortCategoryName(string categoryName)
	{
		// 最後のドット以降のみを取得
		var lastDot = categoryName.LastIndexOf('.');
		return lastDot >= 0 ? categoryName[(lastDot + 1)..] : categoryName;
	}

	private void UpdateMetrics(FrameRenderMetrics? latest)
	{
		if (latest == null)
		{
			TotalFrameTime = "-";
			Zoom = "-";
			IsNavigating = "-";
			Timestamp = "-";
			LayerMetrics.Clear();
			return;
		}

		TotalFrameTime = $"{latest.TotalFrameTime.TotalMilliseconds:F2} ms";
		Zoom = $"{latest.Zoom:F2}";
		IsNavigating = latest.IsNavigating ? "はい" : "いいえ";
		Timestamp = latest.Timestamp.ToString("yyyy/MM/dd HH:mm:ss");

		LayerMetrics.Clear();
		foreach (var layer in latest.LayerMetrics)
		{
			var vm = new LayerMetricsViewModel
			{
				LayerName = layer.LayerName,
				RenderTime = $"{layer.RenderTime.TotalMilliseconds:F2} ms",
				RenderInfo = layer.RenderInfo != null
					? string.Join(", ", layer.RenderInfo.Select(kv => $"{kv.Key}: {kv.Value}"))
					: "-"
			};
			LayerMetrics.Add(vm);
		}
	}
}

public class LayerMetricsViewModel : ReactiveObject
{
	private string _layerName = string.Empty;
	public string LayerName
	{
		get => _layerName;
		set => this.RaiseAndSetIfChanged(ref _layerName, value);
	}

	private string _renderTime = string.Empty;
	public string RenderTime
	{
		get => _renderTime;
		set => this.RaiseAndSetIfChanged(ref _renderTime, value);
	}

	private string _renderInfo = string.Empty;
	public string RenderInfo
	{
		get => _renderInfo;
		set => this.RaiseAndSetIfChanged(ref _renderInfo, value);
	}
}

public class LogEntryViewModel : ReactiveObject
{
	private string _timestamp = string.Empty;
	public string Timestamp
	{
		get => _timestamp;
		set => this.RaiseAndSetIfChanged(ref _timestamp, value);
	}

	private string _logLevel = string.Empty;
	public string LogLevel
	{
		get => _logLevel;
		set => this.RaiseAndSetIfChanged(ref _logLevel, value);
	}

	private string _category = string.Empty;
	public string Category
	{
		get => _category;
		set => this.RaiseAndSetIfChanged(ref _category, value);
	}

	private string _message = string.Empty;
	public string Message
	{
		get => _message;
		set => this.RaiseAndSetIfChanged(ref _message, value);
	}

	private string? _exception;
	public string? Exception
	{
		get => _exception;
		set => this.RaiseAndSetIfChanged(ref _exception, value);
	}
}
