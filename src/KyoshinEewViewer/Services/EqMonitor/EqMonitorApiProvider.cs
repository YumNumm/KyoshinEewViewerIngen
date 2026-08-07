using KyoshinEewViewer.Core;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.EqMonitorApi.Generated;
using ReactiveUI;
using Splat;
using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Reflection;

namespace KyoshinEewViewer.Services.EqMonitor;

/// <summary>
/// EQMonitor API のクライアントを設定に応じて提供する
/// </summary>
/// <remarks>
/// 接続先は配布物に一切含めず、利用者が設定した BaseUrl のみを使う。
/// BaseUrl が未設定または不正な場合はクライアントを提供しない
/// </remarks>
public class EqMonitorApiProvider : ReactiveObject, IDisposable
{
	/// <summary>
	/// アセンブリバージョン<br/>
	/// common.props により Major.Minor.Build がアプリのバージョン、Revision がビルド番号になる
	/// </summary>
	private static Version AssemblyVersion { get; } =
		Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

	/// <summary>
	/// EQMonitor 側で識別するためのプラットフォーム名
	/// </summary>
	private static string PlatformName =>
		OperatingSystem.IsAndroid() ? "Android" :
		OperatingSystem.IsIOS() ? "iOS" :
		OperatingSystem.IsMacOS() ? "macOS" :
		OperatingSystem.IsWindows() ? "Windows" :
		OperatingSystem.IsBrowser() ? "Browser" :
		OperatingSystem.IsLinux() ? "Linux" :
		"Unknown";

	public static string UserAgent { get; } =
		$"KyoshinEewViewer-{PlatformName}-v{AssemblyVersion.Major}.{AssemblyVersion.Minor}.{AssemblyVersion.Build}";

	public static string BuildNumber { get; } =
		AssemblyVersion.Revision.ToString(CultureInfo.InvariantCulture);

	private ILogger Logger { get; }
	private KyoshinEewViewerConfiguration Config { get; }

	private string? _appliedBaseUrl;
	private HttpClient? _httpClient;
	private EqMonitorApiClient? _client;

	public EqMonitorApiProvider(ILogManager logManager, KyoshinEewViewerConfiguration config)
	{
		SplatRegistrations.RegisterLazySingleton<EqMonitorApiProvider>();

		Logger = logManager.GetLogger<EqMonitorApiProvider>();
		Config = config;
	}

	/// <summary>
	/// 現在の設定に対応したクライアントを取得する
	/// </summary>
	/// <returns>接続先が未設定もしくは不正な場合は null</returns>
	public EqMonitorApiClient? GetClient()
	{
		var baseUrl = Config.EqMonitor.BaseUrl?.Trim();
		if (string.IsNullOrEmpty(baseUrl))
		{
			Reset();
			return null;
		}

		// HttpClient.BaseAddress は末尾にスラッシュがないと最後のパス要素が失われる
		if (!baseUrl.EndsWith('/'))
			baseUrl += '/';

		// 設定が変わっていなければ既存のクライアントを使い回す
		if (_appliedBaseUrl == baseUrl)
			return _client;

		if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
		{
			Logger.LogWarning("EQMonitor API の接続先が正しくないため利用できません");
			Reset();
			return null;
		}

		// BaseAddress はリクエスト送信後に変更できないため、接続先ごとに作り直す
		Reset();
		_httpClient = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		})
		{
			BaseAddress = uri,
			Timeout = TimeSpan.FromSeconds(10),
		};
		_httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
		_httpClient.DefaultRequestHeaders.TryAddWithoutValidation("x-eqmonitor-build", BuildNumber);

		_appliedBaseUrl = baseUrl;
		_client = new EqMonitorApiClient(_httpClient);
		Logger.LogInfo("EQMonitor API の接続先を設定しました");
		return _client;
	}

	/// <summary>
	/// EQMonitor API を利用できる状態か
	/// </summary>
	public bool IsAvailable => Config.EqMonitor.Enable && GetClient() != null;

	private void Reset()
	{
		_httpClient?.Dispose();
		_httpClient = null;
		_client = null;
		_appliedBaseUrl = null;
	}

	public void Dispose()
	{
		Reset();
		GC.SuppressFinalize(this);
	}
}
