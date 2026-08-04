using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Splat;
using System;
using System.IO;
using System.Threading.Tasks;

namespace KyoshinEewViewer;

public static class UrlOpener
{
	/// <summary>
	/// URL またはローカルのファイル･ディレクトリを OS の既定のアプリで開く
	/// </summary>
	public static void OpenUrl(string url)
		=> Dispatcher.UIThread.Post(() => _ = OpenUrlAsync(url));

	/// <remarks>
	/// Process.Start をプラットフォームごとに切り替える実装では iOS がどの分岐にも該当せず何も起きないため、
	/// 全プラットフォームに実装のある Avalonia の Launcher に統一している。
	/// Launcher は UI スレッドから呼び出す必要がある
	/// </remarks>
	private static async Task OpenUrlAsync(string url)
	{
		try
		{
			if (KyoshinEewViewerApp.TopLevelControl?.Launcher is not { } launcher)
			{
				LogHost.Default.Warn($"Launcher が利用できないため {url} を開けませんでした");
				return;
			}

			var isOpened = url switch {
				_ when Directory.Exists(url) => await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(url)),
				_ when File.Exists(url) => await launcher.LaunchFileInfoAsync(new FileInfo(url)),
				_ when Uri.TryCreate(url, UriKind.Absolute, out var uri) => await launcher.LaunchUriAsync(uri),
				_ => false,
			};
			if (!isOpened)
				LogHost.Default.Warn($"{url} を開けませんでした");
		}
		catch (Exception ex)
		{
			LogHost.Default.Warn(ex, "URLオープンに失敗しました");
		}
	}
}
