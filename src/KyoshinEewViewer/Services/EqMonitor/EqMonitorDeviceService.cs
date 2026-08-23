using KyoshinEewViewer.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Services.EqMonitor;

/// <summary>
/// EQMonitor API の端末登録と端末 ID の永続化を管理する
/// </summary>
public sealed class EqMonitorDeviceService : IDisposable
{
	private EqMonitorApiProvider ApiProvider { get; }
	private KyoshinEewViewerConfiguration Config { get; }
	private Action<KyoshinEewViewerConfiguration> SaveConfiguration { get; }
	private SemaphoreSlim RegistrationLock { get; } = new(1, 1);

	public EqMonitorDeviceService(
		EqMonitorApiProvider apiProvider,
		KyoshinEewViewerConfiguration config)
		: this(apiProvider, config, ConfigurationLoader.Save)
	{
	}

	internal EqMonitorDeviceService(
		EqMonitorApiProvider apiProvider,
		KyoshinEewViewerConfiguration config,
		Action<KyoshinEewViewerConfiguration> saveConfiguration)
	{
		ApiProvider = apiProvider;
		Config = config;
		SaveConfiguration = saveConfiguration;
	}

	/// <summary>
	/// 現在の接続先で利用できる端末 ID を取得し、未登録なら新規登録する
	/// </summary>
	public async Task<string> GetOrRegisterDeviceIdAsync(CancellationToken cancellationToken)
	{
		await RegistrationLock.WaitAsync(cancellationToken);
		try
		{
			var baseUri = ApiProvider.GetBaseUri()
				?? throw new InvalidOperationException("EQMonitor API の接続先が設定されていません");
			if (!string.IsNullOrWhiteSpace(Config.EqMonitor.DeviceId)
				&& EqMonitorApiProvider.ResolveBaseUri(Config.EqMonitor.DeviceRegisteredBaseUrl, null) == baseUri)
				return Config.EqMonitor.DeviceId;

			var client = ApiProvider.GetClient()
				?? throw new InvalidOperationException("EQMonitor API の接続先が正しくありません");
			var response = await client.PostV2DeviceAsync(new Generated.DeviceRegisterBody
			{
				Type = Generated.DeviceType.DESKTOP,
				Locale = Generated.DeviceLocale.Ja,
			}, cancellationToken);
			if (string.IsNullOrWhiteSpace(response.DeviceId))
				throw new InvalidOperationException("EQMonitor API から端末 ID が返されませんでした");

			// deviceToken は通知 API 用であり、リアルタイム接続には不要なため保存しない
			Config.EqMonitor.DeviceId = response.DeviceId;
			Config.EqMonitor.DeviceRegisteredBaseUrl = baseUri.AbsoluteUri;
			SaveConfiguration(Config);
			return response.DeviceId;
		}
		finally
		{
			RegistrationLock.Release();
		}
	}

	/// <summary>
	/// 保存済み端末 ID を無効化する
	/// </summary>
	public void InvalidateRegistration()
	{
		RegistrationLock.Wait();
		try
		{
			Config.EqMonitor.DeviceId = null;
			Config.EqMonitor.DeviceRegisteredBaseUrl = null;
			SaveConfiguration(Config);
		}
		finally
		{
			RegistrationLock.Release();
		}
	}

	public void Dispose()
		=> RegistrationLock.Dispose();
}
