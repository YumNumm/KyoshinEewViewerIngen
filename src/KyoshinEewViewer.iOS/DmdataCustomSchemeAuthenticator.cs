using Avalonia.Controls;
using DmdataSharp.Authentication.OAuth;
using KyoshinEewViewer.Core;
using KyoshinEewViewer.Services.TelegramPublishers.Dmdata;
using Splat;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace KyoshinEewViewer.iOS;

/// <summary>
/// カスタム URL スキームでコールバックを受ける DMDATA の認可フロー。
/// iOS ではループバック HTTP サーバーを立てられないため、認可ページを外部ブラウザで開き
/// <see cref="RedirectUri"/> でアプリに戻ってきたところを拾う。
/// </summary>
public class DmdataCustomSchemeAuthenticator : IDmdataAuthenticator
{
	/// <summary>
	/// カスタム URL スキームのリダイレクト先を登録した DMDATA のクライアント。
	/// 既定のクライアントはループバック URI しか登録されていないため使えない
	/// </summary>
	public const string ClientId = "CId.jBugjghCHmrapqEeD0IWjfuOy53KYSjH6VEtnFAhFXjD";

	/// <summary>
	/// DMDATA 側のクライアント設定にも同じ値を登録しておく必要がある。
	/// Info.plist の CFBundleURLSchemes にもスキーム部分を登録すること
	/// </summary>
	public const string RedirectUri = "net.yumnumm.kyoshineewviewer://login-callback";

	private static readonly HttpClient HttpClient = new();

	private TaskCompletionSource<Uri>? _callback;
	private string? _expectedState;

	/// <summary>
	/// カスタム URL スキームで開かれたことを通知する。認可待ちでなければ false を返す
	/// </summary>
	public bool HandleCallback(Uri uri)
	{
		var callback = _callback;
		if (callback is null)
		{
			LogHost.Default.Warn($"認可待ちではない状態でコールバックを受けました: {uri.Scheme}://{uri.Host}");
			return false;
		}
		LogHost.Default.Info($"認可コールバックを受け取りました: {uri.Scheme}://{uri.Host}");
		return callback.TrySetResult(uri);
	}

	public async Task<string> AuthorizeAsync(string clientId, string[] scopes, CancellationToken cancellationToken)
	{
		if (_callback is not null)
			throw new InvalidOperationException("認可処理が既に実行中です");

		// DmdataSharp と同じ方式: 乱数の 16 進文字列を verifier、その SHA-256 を base64url で challenge とする
		var verifier = Convert.ToHexString(RandomNumberGenerator.GetBytes(64)).ToLowerInvariant();
		var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)))
			.Replace("=", "").Replace("+", "-").Replace("/", "_");
		var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

		var query = new Dictionary<string, string> {
			{ "client_id", clientId },
			{ "response_type", "code" },
			{ "redirect_uri", RedirectUri },
			{ "response_mode", "query" },
			{ "scope", string.Join(" ", scopes) },
			{ "state", state },
			{ "code_challenge", challenge },
			{ "code_challenge_method", "S256" },
		};
		var authorizeUri = new Uri($"{OAuthCredential.AUTH_ENDPOINT_URL}?{BuildQuery(query)}");

		_expectedState = state;
		_callback = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
		try
		{
			using var registration = cancellationToken.Register(() => _callback?.TrySetCanceled(cancellationToken));

			// TopLevelControl は MainView の接続完了時に設定されるが、取り違えを避けるため
			// 表示中のビューからも引けるようにしておく
			var launcher = KyoshinEewViewerApp.TopLevelControl?.Launcher
				?? TopLevel.GetTopLevel(App.MainView)?.Launcher;
			if (launcher is null)
				throw new InvalidOperationException("ブラウザを開けませんでした");
			if (!await launcher.LaunchUriAsync(authorizeUri))
				throw new InvalidOperationException("認可ページを開けませんでした");

			var code = ReadAuthorizationCode(await _callback.Task);
			return await ExchangeCodeAsync(clientId, code, verifier, cancellationToken);
		}
		finally
		{
			_callback = null;
			_expectedState = null;
		}
	}

	private string ReadAuthorizationCode(Uri callbackUri)
	{
		var query = HttpUtility.ParseQueryString(callbackUri.Query);

		if (query["error"] is { } error)
			throw new InvalidOperationException($"認可が拒否されました: {error}");
		// state を検証しないと別のリクエストへの応答を受け入れてしまう
		if (query["state"] != _expectedState)
			throw new InvalidOperationException("state が一致しません");
		return query["code"] ?? throw new InvalidOperationException("認可コードが返されませんでした");
	}

	private static async Task<string> ExchangeCodeAsync(string clientId, string code, string verifier, CancellationToken cancellationToken)
	{
		using var content = new FormUrlEncodedContent(new Dictionary<string, string> {
			{ "client_id", clientId },
			{ "grant_type", "authorization_code" },
			{ "code", code },
			{ "redirect_uri", RedirectUri },
			{ "code_verifier", verifier },
		});
		using var response = await HttpClient.PostAsync(OAuthCredential.TOKEN_ENDPOINT_URL, content, cancellationToken);
		var body = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw new InvalidOperationException($"トークンの取得に失敗しました: {(int)response.StatusCode} {body}");

		using var json = JsonDocument.Parse(body);
		if (!json.RootElement.TryGetProperty("refresh_token", out var refreshToken) || refreshToken.GetString() is not { } value)
			throw new InvalidOperationException("リフレッシュトークンが返されませんでした");
		return value;
	}

	private static string BuildQuery(Dictionary<string, string> parameters)
	{
		var builder = new StringBuilder();
		foreach (var (key, value) in parameters)
		{
			if (builder.Length > 0)
				builder.Append('&');
			builder.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
		}
		return builder.ToString();
	}
}
