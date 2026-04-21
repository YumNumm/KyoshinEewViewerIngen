#!/bin/bash
set -e

echo "🚀 Running KyoshinEewViewer on macOS..."

# プロジェクトルートに移動
cd "$(dirname "$0")"

# アーキテクチャを判定
ARCH=$(uname -m)
if [ "$ARCH" = "arm64" ]; then
    RID="osx-arm64"
else
    RID="osx-x64"
fi

APP_PATH="src/KyoshinEewViewer.Desktop/bin/Debug/net10.0-macos/$RID/KyoshinEewViewer.Desktop"

# ビルド済みかチェック
if [ ! -f "$APP_PATH" ]; then
    echo "❌ App not found. Building first..."
    ./build-macos.sh
fi

# コード署名をチェック（通知機能に必要）
echo "🔐 Checking code signature..."
if ! codesign -v "$APP_PATH" 2>/dev/null; then
    echo "⚠️  App is not signed. Signing for development..."
    codesign -s - -f --deep "$APP_PATH"
    echo "✅ App signed successfully"
fi

# 実行
echo "▶️  Starting app..."
cd src/KyoshinEewViewer.Desktop
./bin/Debug/net10.0-macos/$RID/KyoshinEewViewer.Desktop
