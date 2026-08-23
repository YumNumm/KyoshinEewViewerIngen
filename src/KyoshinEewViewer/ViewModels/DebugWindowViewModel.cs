using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.Core.Models.Events;
using KyoshinEewViewer.Core.Models.Metrics;
using KyoshinEewViewer.Services;
using KyoshinEewViewer.Services.NetworkDebug;
using KyoshinEewViewer.Services.TelegramPublishers.Dmdata;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using KyoshinEewViewer.Core;

namespace KyoshinEewViewer.ViewModels;

public partial class DebugWindowViewModel : ViewModelBase, IDisposable
{
	public string Title => "デバッグウィンドウ";

	public KyoshinEewViewerConfiguration Config { get; }

	private DmdataRedundantTelegramPublisher? DmdataPublisher { get; }

	/// <summary>DM-D.S.S WebSocket の Pong 送信までの遅延（ms）。直接接続時のみ。</summary>
	public long? DmdataLastPongSendMs => DmdataPublisher?.LastPongSendMilliseconds;

	/// <summary>前回 ping からの間隔（秒）。</summary>
	public double? DmdataLastPingIntervalSec => DmdataPublisher?.LastPingIntervalSeconds;

	private PropertyChangedEventHandler? _dmdataPublisherPropertyChanged;

	private IDisposable? _networkSubscription;
	private readonly Subject<NetworkTransactionAdded> _networkEvents = new();
	private bool _isActive;
	private readonly InMemoryLoggerProvider? _loggerProvider;
	private readonly NetworkDebugRecorder? _networkRecorder;

	/// <summary>
	/// 絞り込み前のすべての通信記録
	/// </summary>
	private readonly List<NetworkTransactionViewModel> _allNetworkTransactions = [];

	[ObservableProperty]
	public partial ObservableCollection<LayerMetricsViewModel> LayerMetrics { get; set; } = [];

	[ObservableProperty]
	public partial string TotalFrameTime { get; set; } = "-";

	[ObservableProperty]
	public partial string Zoom { get; set; } = "-";

	[ObservableProperty]
	public partial string IsNavigating { get; set; } = "-";

	[ObservableProperty]
	public partial string Timestamp { get; set; } = "-";

	[ObservableProperty]
	public partial ObservableCollection<LogEntryViewModel> LogEntries { get; set; } = [];

	[ObservableProperty]
	public partial bool AutoScroll { get; set; } = true;

	[ObservableProperty]
	public partial bool ScrollToEnd { get; set; }

	/// <summary>
	/// WebSocket接続先 URL（直接接続モード: ws:// または wss:// で始まる URL）
	/// </summary>
	public string? WebSocketOverrideUrl
	{
		get => Config.Dmdata.WebSocketDefaultEndpoint;
		set
		{
			Config.Dmdata.WebSocketDefaultEndpoint = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
			OnPropertyChanged();
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
			OnPropertyChanged();
		}
	}

	/// <summary>
	/// 絞り込み後の通信記録
	/// </summary>
	[ObservableProperty]
	public partial ObservableCollection<NetworkTransactionViewModel> NetworkTransactions { get; set; } = [];

	[ObservableProperty]
	public partial NetworkTransactionViewModel? SelectedNetworkTransaction { get; set; }

	/// <summary>
	/// 通信内容を記録するか
	/// </summary>
	public bool RecordNetworkTraffic
	{
		get => Config.Debug.RecordNetworkTraffic;
		set
		{
			Config.Debug.RecordNetworkTraffic = value;
			OnPropertyChanged();
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
			if (SetProperty(ref _networkHostFilter, value))
				ApplyNetworkFilter();
		}
	}

	[ObservableProperty]
	public partial bool IsDmdataReconnecting { get; set; }

	[ObservableProperty]
	public partial string? DmdataReconnectStatus { get; set; }

	public DebugWindowViewModel(
		KyoshinEewViewerConfiguration config,
		InMemoryLoggerProvider? loggerProvider = null,
		NetworkDebugRecorder? networkRecorder = null,
		DmdataRedundantTelegramPublisher? dmdataPublisher = null)
	{
		Config = config;
		_loggerProvider = loggerProvider ?? ServiceLocator.Current.GetService<InMemoryLoggerProvider>();
		_networkRecorder = networkRecorder ?? ServiceLocator.Current.GetService<NetworkDebugRecorder>();
		DmdataPublisher = dmdataPublisher ?? ServiceLocator.Current.GetService<DmdataRedundantTelegramPublisher>();
		if (DmdataPublisher is INotifyPropertyChanged dmdataInpc)
		{
			_dmdataPublisherPropertyChanged = (_, e) =>
			{
				if (e.PropertyName is nameof(DmdataRedundantTelegramPublisher.LastPongSendMilliseconds)
				    or nameof(DmdataRedundantTelegramPublisher.LastPingIntervalSeconds))
				{
					OnPropertyChanged(nameof(DmdataLastPongSendMs));
					OnPropertyChanged(nameof(DmdataLastPingIntervalSec));
				}
			};
			dmdataInpc.PropertyChanged += _dmdataPublisherPropertyChanged;
		}

		// メトリクス更新イベントをサブスクライブ (UI スレッドへマーシャリングする)
		StrongReferenceMessenger.Default.Register<MetricsUpdated>(this,
			(_, msg) => Dispatcher.UIThread.Post(() => UpdateMetrics(msg.Metrics)));

		// ログ追加イベントをサブスクライブ (UI スレッドへマーシャリングする)
		StrongReferenceMessenger.Default.Register<LogEntryAdded>(this,
			(_, msg) => Dispatcher.UIThread.Post(() => AddLogEntry(msg.Entry)));

		// 通信記録は強震モニタにより毎秒発生するため、まとめて追加して描画負荷を抑える
		StrongReferenceMessenger.Default.Register<NetworkTransactionAdded>(this,
			(_, msg) => _networkEvents.OnNext(msg));
		_networkSubscription = _networkEvents
			.Buffer(TimeSpan.FromMilliseconds(250))
			.Where(messages => messages.Count > 0)
			.Subscribe(messages => Dispatcher.UIThread.Post(() => AddNetworkTransactions(messages)));

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

		StrongReferenceMessenger.Default.Send(new MapImageSaveRequested { TargetPath = file.Path.LocalPath });
	}

	/// <summary>
	/// メトリクス収集を有効化
	/// </summary>
	public void Activate()
	{
		if (_isActive) return;
		_isActive = true;
		StrongReferenceMessenger.Default.Send(new MetricsEnabledChanged { IsEnabled = true });
	}

	/// <summary>
	/// メトリクス収集を無効化
	/// </summary>
	public void Deactivate()
	{
		if (!_isActive) return;
		_isActive = false;
		StrongReferenceMessenger.Default.Send(new MetricsEnabledChanged { IsEnabled = false });
	}

	public void Dispose()
	{
		Deactivate();
		if (DmdataPublisher is INotifyPropertyChanged dmdataInpc && _dmdataPublisherPropertyChanged is { } h)
			dmdataInpc.PropertyChanged -= h;
		_networkSubscription?.Dispose();
		_networkEvents.Dispose();
		StrongReferenceMessenger.Default.UnregisterAll(this);
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

public partial class LayerMetricsViewModel : ObservableObject
{
	[ObservableProperty]
	public partial string LayerName { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string RenderTime { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string RenderInfo { get; set; } = string.Empty;
}

public partial class LogEntryViewModel : ObservableObject
{
	[ObservableProperty]
	public partial string Timestamp { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string LogLevel { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string Category { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string Message { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string? Exception { get; set; }
}
