using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace KyoshinEewViewer.Services.NetworkDebug;

/// <summary>
/// 記録内容から認証情報を取り除く
/// </summary>
public static partial class NetworkCaptureMasker
{
	private const string Mask = "***";

	/// <summary>
	/// 値を伏せるヘッダ
	/// </summary>
	private static readonly string[] SensitiveHeaderNames = [
		"Authorization",
		"Proxy-Authorization",
		"Cookie",
		"Set-Cookie",
	];

	/// <summary>
	/// JSON ボディ内で値を伏せるキー<br/>
	/// 気象庁のデータは <c>code</c> を地域コード等で多用するため、単独の <c>code</c> は対象にしない
	/// </summary>
	[GeneratedRegex("""
		"(access_token|refresh_token|id_token|client_secret|code_verifier)"\s*:\s*"(?:\\.|[^"\\])*"
		""", RegexOptions.IgnoreCase)]
	private static partial Regex SensitiveJsonValueRegex();

	/// <summary>
	/// フォームエンコードされたボディ内で値を伏せるキー<br/>
	/// DM-D.S.S のトークン要求は <c>code</c> に認可コードを載せるため、こちらは対象に含める
	/// </summary>
	[GeneratedRegex("""
		(^|&)(access_token|refresh_token|id_token|client_secret|code_verifier|code)=[^&]*
		""", RegexOptions.IgnoreCase)]
	private static partial Regex SensitiveFormValueRegex();

	public static bool IsSensitiveHeader(string name)
		=> SensitiveHeaderNames.Contains(name, StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// ヘッダを記録用の形へ変換する。認証情報を含むヘッダは値を伏せる
	/// </summary>
	public static List<KeyValuePair<string, string>> MaskHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>>? headers)
	{
		var result = new List<KeyValuePair<string, string>>();
		if (headers is null)
			return result;

		foreach (var (name, values) in headers)
			result.Add(new(name, IsSensitiveHeader(name) ? Mask : string.Join(", ", values)));
		return result;
	}

	/// <summary>
	/// 接続先から認証情報を取り除く<br/>
	/// DM-D.S.S の WebSocket URL はクエリに ticket を載せるため、クエリ全体を落とす
	/// </summary>
	public static string SanitizeEndpoint(string endpoint)
	{
		var queryIndex = endpoint.IndexOf('?');
		return queryIndex >= 0 ? endpoint[..queryIndex] : endpoint;
	}

	/// <summary>
	/// ボディから認証情報を取り除く
	/// </summary>
	public static string MaskBody(string body)
	{
		if (string.IsNullOrEmpty(body))
			return body;

		var masked = SensitiveJsonValueRegex().Replace(body, $"\"$1\": \"{Mask}\"");
		return SensitiveFormValueRegex().Replace(masked, $"$1$2={Mask}");
	}
}
