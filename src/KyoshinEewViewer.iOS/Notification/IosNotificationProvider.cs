using Foundation;
using KyoshinEewViewer.Core;
using KyoshinEewViewer.Notification;
using Microsoft.Extensions.Logging;
using System;
using UserNotifications;

namespace KyoshinEewViewer.iOS.Notification;

/// <summary>
/// UNUserNotificationCenter を使った iOS 向けの通知実装。
///
/// iOS はバックグラウンドに回ったアプリを速やかにサスペンドするため、強震モニタや
/// 電文の受信自体が止まる。プッシュ配信サーバーを持たない構成では、通知が発生するのは
/// アプリがフォアグラウンドにある間だけになる。
/// またフォアグラウンドでは既定でバナーが抑制されるため、
/// <see cref="ForegroundPresenter"/> で明示的に表示させている。
/// </summary>
public class IosNotificationProvider : NotificationProvider
{
	private readonly ForegroundPresenter _presenter = new();
	private bool _authorized;

	public IosNotificationProvider()
	{
		UNUserNotificationCenter.Current.Delegate = _presenter;

		// Critical は権限が別枠なので併せて要求する。拒否されても通常の通知だけは動く
		UNUserNotificationCenter.Current.RequestAuthorization(
			UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound | UNAuthorizationOptions.Badge | UNAuthorizationOptions.CriticalAlert,
			(granted, error) =>
			{
				_authorized = granted;
				if (error != null)
					AppLog.Default.LogWarning("通知の許可要求に失敗しました: {Error}", error.LocalizedDescription);
				else if (!granted)
					AppLog.Default.LogWarning("通知が許可されませんでした");
			});
	}

	public override void SendNotice(NotificationRequest request)
	{
		if (!_authorized)
			return;

		using var content = new UNMutableNotificationContent
		{
			Title = request.Title,
			Body = request.Message,
		};

		if (request.Urgency == NotificationUrgency.Critical)
		{
			// おやすみモードと消音を貫通させる
			content.InterruptionLevel = UNNotificationInterruptionLevel.Critical;
			content.Sound = UNNotificationSound.DefaultCriticalSound;
		}
		else
		{
			content.InterruptionLevel = request.Urgency == NotificationUrgency.Low
				? UNNotificationInterruptionLevel.Passive
				: UNNotificationInterruptionLevel.TimeSensitive;
			content.Sound = UNNotificationSound.Default;
		}

		// trigger が null なら即時配信される
		var notification = UNNotificationRequest.FromIdentifier(Guid.NewGuid().ToString(), content, null);
		UNUserNotificationCenter.Current.AddNotificationRequest(notification, error =>
		{
			if (error != null)
				AppLog.Default.LogWarning("通知の送信に失敗しました: {Error}", error.LocalizedDescription);
		});
	}

	public override void Dispose()
	{
		UNUserNotificationCenter.Current.Delegate = null;
		_presenter.Dispose();
		GC.SuppressFinalize(this);
	}

	/// <summary>
	/// アプリがフォアグラウンドにある間もバナーと音を出すためのデリゲート。
	/// これが無いと表示されるのはアプリが背面にある時だけになり、本アプリでは通知が一切出ない
	/// </summary>
	private sealed class ForegroundPresenter : UNUserNotificationCenterDelegate
	{
		public override void WillPresentNotification(UNUserNotificationCenter center, UNNotification notification, Action<UNNotificationPresentationOptions> completionHandler)
			=> completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound | UNNotificationPresentationOptions.List);
	}
}
