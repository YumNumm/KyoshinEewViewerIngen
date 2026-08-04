using System.Threading;
using System.Threading.Tasks;

namespace KyoshinEewViewer.Services.TelegramPublishers.Dmdata;

/// <summary>
/// DMDATA の認可フローを提供する。
/// 既定の実装 (DmdataSharp の SimpleOAuthAuthenticator) はループバック HTTP で
/// コールバックを受けるため、それが使えないプラットフォームがこれを差し替える。
/// </summary>
public interface IDmdataAuthenticator
{
	/// <summary>
	/// 認可を行い、リフレッシュトークンを返す
	/// </summary>
	Task<string> AuthorizeAsync(string clientId, string[] scopes, CancellationToken cancellationToken);
}
