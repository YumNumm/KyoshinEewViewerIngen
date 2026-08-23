using System;

namespace KyoshinEewViewer.Core;

/// <summary>
/// アプリ全体で共有する DI コンテナへの静的アクセスポイント。
/// コンストラクタインジェクションで受け取れない箇所 (View のコードビハインドなど) から利用する。
/// </summary>
public static class ServiceLocator
{
	private static IServiceProvider? _current;

	/// <summary>
	/// 現在の <see cref="IServiceProvider"/>
	/// </summary>
	public static IServiceProvider Current
		=> _current ?? throw new InvalidOperationException("DI コンテナが初期化されていません");

	/// <summary>
	/// DI コンテナが初期化済みの場合のみ現在の provider を返す。
	/// 初期化前でも動作する任意機能の遅延解決に使用する。
	/// </summary>
	public static IServiceProvider? CurrentOrNull => _current;

	/// <summary>
	/// アプリ起動時に構築した <see cref="IServiceProvider"/> を登録する
	/// </summary>
	public static void SetProvider(IServiceProvider provider)
		=> _current = provider;
}
