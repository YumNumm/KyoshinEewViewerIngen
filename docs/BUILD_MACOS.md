# macOS ビルドガイド

## 概要

KyoshinEewViewer for ingen は macOS 通知機能をサポートしています。このドキュメントでは、macOS でのビルド方法と既知の問題について説明します。

## 必要な環境

- **macOS**: 10.14 (Mojave) 以降
- **.NET SDK**: 10.0.102 以降
- **Xcode**: 14.0 以降
- **macOS ワークロード**: `dotnet workload install macos`

## 既知の問題: Avalonia 11.3.9 マルチターゲットビルド制限

現在、Avalonia 11.3.9 にはマルチターゲットプロジェクト（`net10.0;net10.0-macos`）のビルドに関する制限があります。

### エラー内容

```
error MSB4022: 要素 <UsingTask> 内の属性 "AssemblyFile" の値 "$(AvaloniaBuildTasksLocation)" を評価した結果 "" は無効です。
```

### 回避策

以下のいずれかの方法でビルドしてください：

#### 方法1: 個別ターゲットビルド（推奨）

```bash
# KyoshinEewViewer を net10.0-macos でビルド
cd src/KyoshinEewViewer
dotnet build -f net10.0-macos -r osx-arm64  # Apple Silicon
dotnet build -f net10.0-macos -r osx-x64    # Intel Mac

# Desktop プロジェクトは現在調査中
# 回避策が確立次第、このドキュメントを更新します
```

#### 方法2: キャッシュクリアからのビルド

```bash
# 全てクリーン
dotnet nuget locals all --clear
find src -name "obj" -type d -exec rm -rf {} + 2>/dev/null
find src -name "bin" -type d -exec rm -rf {} + 2>/dev/null

# 復元とビルド
cd src/KyoshinEewViewer
dotnet restore
dotnet build -f net10.0-macos -r osx-arm64
```

## 通知機能について

macOS 通知機能は `UNUserNotificationCenter` API を使用しています。

### 機能

- ✅ 地震情報の通知
- ✅ 緊急地震速報の通知
- ✅ 通知権限の管理（設定画面から）
- ❌ トレイアイコン機能（未実装、Linux と同様）

### 権限リクエスト

通知を表示するには、ユーザーからの明示的な権限が必要です：

1. アプリを起動
2. 設定 → 通知 → 「macOS 通知設定」
3. 「権限をリクエスト」ボタンをクリック
4. macOS のシステムダイアログで「許可」を選択

### コード署名

通知機能を使用するには、アプリにコード署名が必要です：

```bash
# 開発用の簡易署名
codesign -s - -f --deep [アプリのパス]
```

## トラブルシューティング

### Q: ビルドが失敗する

A: 以下を順番に試してください：
1. `dotnet workload install macos` でワークロードをインストール
2. `dotnet nuget locals all --clear` でキャッシュをクリア
3. obj/bin ディレクトリを削除
4. 上記の「個別ターゲットビルド」を実行

### Q: 通知が表示されない

A: 以下を確認してください：
1. アプリがコード署名されているか
2. システム環境設定で通知が許可されているか
3. アプリの設定画面で通知権限の状態が「✓ 許可済み」になっているか

## 今後の予定

- [ ] Avalonia の次期バージョンでのマルチターゲットビルド問題の解決を待つ
- [ ] Desktop プロジェクトのビルド回避策を確立
- [ ] メニューバーアイコン機能の実装（将来的に）

## 関連リンク

- [Avalonia UI](https://avaloniaui.net/)
- [Apple UserNotifications Framework](https://developer.apple.com/documentation/usernotifications)
- [.NET macOS ワークロード](https://learn.microsoft.com/en-us/dotnet/maui/macos/)
