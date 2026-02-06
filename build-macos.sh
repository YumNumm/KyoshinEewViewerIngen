#!/bin/bash
set -e

echo "🍎 Building KyoshinEewViewer for macOS..."

# プロジェクトルートに移動
cd "$(dirname "$0")"

# アーキテクチャを判定
ARCH=$(uname -m)
if [ "$ARCH" = "arm64" ]; then
    RID="osx-arm64"
    echo "📱 Detected Apple Silicon (M1/M2/M3)"
else
    RID="osx-x64"
    echo "💻 Detected Intel Mac"
fi

# ステップ1: KyoshinEewViewer プロジェクトをビルド
echo "🔨 Building KyoshinEewViewer..."
cd src/KyoshinEewViewer
dotnet build -f net10.0-macos -r "$RID" --no-restore

# ステップ2: Desktop プロジェクトをビルド
echo "🔨 Building Desktop..."
cd ../KyoshinEewViewer.Desktop
dotnet build -f net10.0-macos -r "$RID"

echo "✅ Build completed!"
echo "📦 Output: src/KyoshinEewViewer.Desktop/bin/Debug/net10.0-macos/$RID/"
echo ""
echo "To run the app:"
echo "  cd src/KyoshinEewViewer.Desktop"
echo "  ./bin/Debug/net10.0-macos/$RID/KyoshinEewViewer.Desktop"
