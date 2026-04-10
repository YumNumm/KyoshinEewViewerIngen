#if MACOS
using Foundation;
using Splat;
using System;
using System.Threading.Tasks;
using UserNotifications;

namespace KyoshinEewViewer.Notification.macOS;

/// <summary>
/// macOS 向けの通知プロバイダー (UNUserNotificationCenter 使用)
/// </summary>
public class MacOSNotificationProvider : NotificationProvider
{
	// トレイアイコン機能は未実装 (Linux と同様)
	public override bool TrayIconAvailable => false;

	private bool _isAuthorized = false;

	public override void InitializeTrayIcon(TrayMenuItem[] menuItems)
	{
		// macOS ではトレイアイコン機能は未実装
		// 何もしない
	}

	/// <summary>
	/// 通知権限をリクエストする (非同期)
	/// </summary>
	public async Task<bool> RequestAuthorizationAsync()
	{
		try
		{
			var center = UNUserNotificationCenter.Current;
			var (granted, error) = await center.RequestAuthorizationAsync(
				UNAuthorizationOptions.Alert |
				UNAuthorizationOptions.Sound |
				UNAuthorizationOptions.Badge
			);

			if (error != null)
			{
				LogHost.Default.Error($"通知権限のリクエストに失敗: {error.LocalizedDescription}");
				return false;
			}

			_isAuthorized = granted;
			if (granted)
				LogHost.Default.Info("通知権限が許可されました");
			else
				LogHost.Default.Warn("通知権限が拒否されました");

			return granted;
		}
		catch (Exception ex)
		{
			LogHost.Default.Error(ex, "通知権限のリクエスト中に例外が発生");
			return false;
		}
	}

	/// <summary>
	/// 現在の通知権限の状態を確認する (非同期)
	/// </summary>
	public async Task<UNAuthorizationStatus> GetAuthorizationStatusAsync()
	{
		try
		{
			var settings = await UNUserNotificationCenter.Current.GetNotificationSettingsAsync();
			return settings.AuthorizationStatus;
		}
		catch (Exception ex)
		{
			LogHost.Default.Error(ex, "通知権限の確認中に例外が発生");
			return UNAuthorizationStatus.NotDetermined;
		}
	}

	public override void SendNotice(string title, string message)
	{
		// 同期メソッドから非同期メソッドを呼び出す
		// 既存の NotificationProvider インターフェースが同期のため、
		// Task.Run でバックグラウンド実行
		_ = Task.Run(async () => await SendNoticeAsync(title, message));
	}

	/// <summary>
	/// 通知を送信する (非同期版)
	/// </summary>
	private async Task SendNoticeAsync(string title, string message)
	{
		try
		{
			// 権限が未確認の場合は状態を確認
			if (!_isAuthorized)
			{
				var status = await GetAuthorizationStatusAsync();
				if (status == UNAuthorizationStatus.NotDetermined)
				{
					// 権限が未決定の場合は自動的にリクエスト
					await RequestAuthorizationAsync();
				}
				else if (status == UNAuthorizationStatus.Authorized)
				{
					_isAuthorized = true;
				}
				else
				{
					// 拒否されている場合はログ出力して終了
					LogHost.Default.Debug("通知権限が拒否されているため、通知を送信できません");
					return;
				}
			}

			if (!_isAuthorized)
			{
				LogHost.Default.Debug("通知権限がないため、通知を送信しません");
				return;
			}

			// 通知コンテンツの作成
			var content = new UNMutableNotificationContent
			{
				Title = title,
				Body = message,
				Sound = UNNotificationSound.Default
			};

			// 通知リクエストの作成 (即座に表示)
			var request = UNNotificationRequest.FromIdentifier(
				Guid.NewGuid().ToString(),
				content,
				null // trigger が null の場合は即座に表示
			);

			// 通知センターに追加
			await UNUserNotificationCenter.Current.AddNotificationRequestAsync(request);
			LogHost.Default.Debug($"通知を送信しました: {title}");
		}
		catch (Exception ex)
		{
			LogHost.Default.Error(ex, "通知の送信中に例外が発生");
		}
	}

	public override void Dispose() => GC.SuppressFinalize(this);
}
#endif
