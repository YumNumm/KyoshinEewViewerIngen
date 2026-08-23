using KyoshinEewViewer.Core;
using KyoshinEewViewer.Core.Models;
using KyoshinEewViewer.EqMonitorApi.Generated;
using KyoshinEewViewer.Services.NetworkDebug;
using ReactiveUI;
using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;

namespace KyoshinEewViewer.Services.EqMonitor;

/// <summary>
/// EQMonitor API のクライアントを設定に応じて提供する
/// </summary>
/// <remarks>
/// 接続先はソースに書かず、利用者が設定した BaseUrl か、配信ワークフローがビルド時に
/// 埋め込んだ既定値のいずれかを使う。どちらも無い場合はクライアントを提供しない
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

	/// <summary>
	/// 配信ワークフローがビルド時に埋め込んだ既定の接続先<br/>
	/// 埋め込まれていないビルドでは null
	/// </summary>
	public static string? BuiltInBaseUrl { get; } = Assembly.GetExecutingAssembly()
		.GetCustomAttributes<AssemblyMetadataAttribute>()
		.FirstOrDefault(a => a.Key == "EqMonitorBaseUrl")?.Value;

	private ILogger Logger { get; }
	private KyoshinEewViewerConfiguration Config { get; }

	private Uri? _appliedBaseUri;
	private HttpClient? _httpClient;
	private EqMonitorApiClient? _client;

	public EqMonitorApiProvider(ILogger<EqMonitorApiProvider> logger, KyoshinEewViewerConfiguration config)
	{
		Logger = logger;
		Config = config;
	}

	/// <summary>
	/// 利用する接続先を決定する<br/>
	/// 利用者が設定した接続先を優先し、未設定であればビルド時に埋め込まれた既定値へフォールバックする
	/// </summary>
	/// <param name="configured">利用者が設定した接続先</param>
	/// <param name="builtIn">ビルド時に埋め込まれた既定の接続先</param>
	/// <returns>どちらも未設定、もしくは接続先が不正な場合は null</returns>
	internal static Uri? ResolveBaseUri(string? configured, string? builtIn)
	{
		var baseUrl = configured?.Trim();
		if (string.IsNullOrEmpty(baseUrl))
			baseUrl = builtIn?.Trim();
		if (string.IsNullOrEmpty(baseUrl))
			return null;

		// HttpClient.BaseAddress は末尾にスラッシュがないと最後のパス要素が失われる
		if (!baseUrl.EndsWith('/'))
			baseUrl += '/';

		if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
			return null;

		return uri;
	}

	/// <summary>
	/// 現在の設定に対応したクライアントを取得する
	/// </summary>
	/// <returns>接続先が未設定もしくは不正な場合は null</returns>
	public EqMonitorApiClient? GetClient()
	{
		var uri = ResolveBaseUri(Config.EqMonitor.BaseUrl, BuiltInBaseUrl);
		if (uri == null)
		{
			// 接続先が一つも与えられていない状態は異常ではないため警告しない
			if (!string.IsNullOrWhiteSpace(Config.EqMonitor.BaseUrl) || !string.IsNullOrWhiteSpace(BuiltInBaseUrl))
				Logger.LogWarning("EQMonitor API の接続先が正しくないため利用できません");
			Reset();
			return null;
		}

		// 設定が変わっていなければ既存のクライアントを使い回す
		if (_appliedBaseUri == uri)
			return _client;

		// BaseAddress はリクエスト送信後に変更できないため、接続先ごとに作り直す
		Reset();
		_httpClient = NetworkDebugHttpClient.Create(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		}, TimeSpan.FromSeconds(10));
		_httpClient.BaseAddress = uri;
		_httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
		_httpClient.DefaultRequestHeaders.TryAddWithoutValidation("x-eqmonitor-build", BuildNumber);

		_appliedBaseUri = uri;
		_client = new EqMonitorApiClient(_httpClient);
		Logger.LogInformation("EQMonitor API の接続先を設定しました");
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
		_appliedBaseUri = null;
	}

	public void Dispose()
	{
		Reset();
		GC.SuppressFinalize(this);
	}
}
