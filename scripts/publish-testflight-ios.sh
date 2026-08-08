#!/usr/bin/env bash
#
# KyoshinEewViewer iOS 版を App Store Connect (以下 ASC) 向けに ipa 化し、
# TestFlight へアップロードするスクリプト。
#
# ローカルでも GitHub Actions でも同じ手順で動くよう、入力は全て環境変数で受け取る。
# ASC 周りの前提 (アプリレコード / API キー / asc CLI の罠) は macOS 版と共通なので
# docs/TESTFLIGHT_MACOS.md を参照。CI から呼ぶ場合は .github/workflows/deploy-app.yaml を参照。
#
# 使い方:
#   APP_ID=1234567890 ./scripts/publish-testflight-ios.sh
#   APP_ID=1234567890 SKIP_UPLOAD=true ./scripts/publish-testflight-ios.sh   # ipa 作成まで
#
# 署名について:
#   iOS 配布証明書 (.p12) やプロビジョニングプロファイルは一切用意しない。
#   無署名の .app から .xcarchive を組み立て、xcodebuild -exportArchive に
#   -allowProvisioningUpdates と ASC API キーを渡して Apple 側に証明書と
#   プロファイルを自動発行させる (Cloud-managed certificates)。
#
set -euo pipefail

# ---------------------------------------------------------------------------
# パラメータ (全て環境変数で上書き可能)
# ---------------------------------------------------------------------------

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# net.yumnumm.KyoshinEewViewer のアプリレコード (docs/TESTFLIGHT_MACOS.md の
# 「アプリレコードの取り違え注意」参照。旧 net.yumnumm.kevi のものとは別)
APP_ID="${APP_ID:-6797931103}"
BUNDLE_ID="${BUNDLE_ID:-net.yumnumm.KyoshinEewViewer}"
TEAM_ID="${TEAM_ID:-CPL7H8SHVM}"

# バージョン。APP_VERSION は CFBundleShortVersionString、BUILD_NUMBER は CFBundleVersion になる。
# BUILD_NUMBER 未指定時は asc に問い合わせて次に使える番号を取得する。
APP_VERSION="${APP_VERSION:-1.0}"
BUILD_NUMBER="${BUILD_NUMBER:-}"

CONFIGURATION="${CONFIGURATION:-Release}"
TFM="${TFM:-net10.0-ios}"
RID="${RID:-ios-arm64}"

PROJECT_PATH="${PROJECT_PATH:-$REPO_ROOT/src/KyoshinEewViewer.iOS/KyoshinEewViewer.iOS.csproj}"
EXPORT_OPTIONS_PLIST="${EXPORT_OPTIONS_PLIST:-$REPO_ROOT/build-files/ios/ExportOptions.plist}"

# 出力先。既定はリポジトリ直下の out/ (git-ignore 済み)
OUTPUT_DIR="${OUTPUT_DIR:-$REPO_ROOT/out/testflight-ios}"

# asc CLI。mise 管理下のバイナリを優先する。
ASC_BIN="${ASC_BIN:-}"

# TestFlight のテスターグループ。指定すると asc publish testflight で配布まで行う。
# なお CD が付ける変更履歴 (テスト対象の末尾の rev マーカー) はここでは付かないため、
# 手動配信を挟むと次回の CD が差分の起点を 1 つ前のビルドまで遡って探すことになる。
# scripts/build-testflight-changelog.sh 参照
TESTFLIGHT_GROUP="${TESTFLIGHT_GROUP:-}"

SKIP_UPLOAD="${SKIP_UPLOAD:-false}"

# ---------------------------------------------------------------------------
# ユーティリティ
# ---------------------------------------------------------------------------

log() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
die() {
	printf '\033[1;31mエラー:\033[0m %s\n' "$*" >&2
	exit 1
}

PLIST_BUDDY=/usr/libexec/PlistBuddy

# asc を解決する。mise 経由 / PATH 上 / 明示指定のいずれでも動くようにする。
resolve_asc() {
	if [ -n "$ASC_BIN" ]; then
		echo "$ASC_BIN"
		return
	fi
	if command -v mise >/dev/null 2>&1; then
		local from_mise
		# mise which は未インストール時に失敗するので、その場合は PATH を見る
		if from_mise="$(mise which asc 2>/dev/null)" && [ -x "$from_mise" ]; then
			echo "$from_mise"
			return
		fi
	fi
	if command -v asc >/dev/null 2>&1; then
		command -v asc
		return
	fi
	die "asc CLI が見つかりません。mise install を実行するか ASC_BIN を指定してください。"
}

# xcodebuild の出力を xcbeautify があれば整形して流す。
# パイプを挟んでも失敗を握り潰さないよう pipefail に依存する。
run_xcodebuild() {
	if command -v xcbeautify >/dev/null 2>&1; then
		xcodebuild "$@" | xcbeautify
	else
		xcodebuild "$@"
	fi
}

# ---------------------------------------------------------------------------
# 事前チェック
# ---------------------------------------------------------------------------

log "事前チェックを実行します"

[ "$(uname -s)" = "Darwin" ] || die "このスクリプトは macOS 上でのみ実行できます。"
[ -f "$EXPORT_OPTIONS_PLIST" ] || die "ExportOptions.plist が見つかりません: $EXPORT_OPTIONS_PLIST"
command -v xcodebuild >/dev/null 2>&1 || die "xcodebuild が見つかりません。Xcode をインストールしてください。"

# エクスポート時のクラウド署名に ASC API キーが必須。asc CLI と同じ環境変数から受け取る。
[ -n "${ASC_KEY_ID:-}" ] || die "ASC_KEY_ID を指定してください。"
[ -n "${ASC_ISSUER_ID:-}" ] || die "ASC_ISSUER_ID を指定してください。"

# xcodebuild は $HOME/.private_keys/AuthKey_<KEY_ID>.p8 を既定の探索先にするため、
# base64 で渡された鍵はそこへ展開する。ASC_PRIVATE_KEY_PATH 指定時はそれを使う。
PRIVATE_KEY_PATH="${ASC_PRIVATE_KEY_PATH:-}"
if [ -z "$PRIVATE_KEY_PATH" ]; then
	[ -n "${ASC_PRIVATE_KEY_B64:-}" ] || die "ASC_PRIVATE_KEY_B64 または ASC_PRIVATE_KEY_PATH を指定してください。"
	PRIVATE_KEY_PATH="$HOME/.private_keys/AuthKey_$ASC_KEY_ID.p8"
	mkdir -p "$HOME/.private_keys"
	# 秘密鍵はログへ出さない。umask で他ユーザーから読めないようにしておく
	(umask 077 && printf '%s' "$ASC_PRIVATE_KEY_B64" | base64 --decode >"$PRIVATE_KEY_PATH")
fi
[ -f "$PRIVATE_KEY_PATH" ] || die "ASC の秘密鍵が見つかりません: $PRIVATE_KEY_PATH"

# asc CLI はキーチェーンではなく環境変数から認証情報を読むようにする
# (キーチェーン方式は GUI の許可ダイアログ待ちでハングし得る。docs/TESTFLIGHT_MACOS.md の「遭遇したエラー 2」)
export ASC_BYPASS_KEYCHAIN="${ASC_BYPASS_KEYCHAIN:-1}"
export ASC_PRIVATE_KEY_PATH="$PRIVATE_KEY_PATH"

ASC="$(resolve_asc)"
log "asc: $ASC"

[ -n "$APP_ID" ] || die "APP_ID を指定してください。$BUNDLE_ID のアプリレコードを ASC で作成し、その App ID を渡します (asc apps list で確認できます)。"

# ---------------------------------------------------------------------------
# ビルド番号の決定
# ---------------------------------------------------------------------------
#
# github.run_number は ASC 側の履歴と無関係なので衝突し得る。ASC に問い合わせるのが安全。

if [ -z "$BUILD_NUMBER" ]; then
	log "ビルド番号を App Store Connect から取得します"
	BUILD_NUMBER="$(
		"$ASC" builds next-build-number \
			--app "$APP_ID" \
			--version "$APP_VERSION" \
			--platform IOS \
			--output json |
			sed -n 's/.*"nextBuildNumber":"\([0-9]*\)".*/\1/p'
	)"
	[ -n "$BUILD_NUMBER" ] || die "ビルド番号の取得に失敗しました。BUILD_NUMBER を明示指定してください。"
fi

log "バージョン: $APP_VERSION / ビルド番号: $BUILD_NUMBER / RID: $RID"

# ---------------------------------------------------------------------------
# 1. dotnet publish (無署名)
# ---------------------------------------------------------------------------
#
# ArchiveOnBuild=true は使えない。iOS SDK は archive の作成に署名を要求するため
# (Xamarin.Shared.targets: "Code signing must be enabled to create an Xcode archive.")、
# EnableCodeSigning=false と併用すると必ず失敗する。
# そのため無署名で .app を作り、後段で .xcarchive を手組みする。

log "無署名で publish します"
rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

dotnet publish "$PROJECT_PATH" \
	-c "$CONFIGURATION" \
	-f "$TFM" \
	-r "$RID" \
	-p:EnableCodeSigning=false \
	-p:ApplicationId="$BUNDLE_ID" \
	-p:ApplicationDisplayVersion="$APP_VERSION" \
	-p:ApplicationVersion="$BUILD_NUMBER"

BUILD_OUTPUT_DIR="$(dirname "$PROJECT_PATH")/bin/$CONFIGURATION/$TFM/$RID"
[ -d "$BUILD_OUTPUT_DIR" ] || die "ビルド出力が見つかりません: $BUILD_OUTPUT_DIR"

# find | head だと head が先に閉じたときに SIGPIPE で pipefail に引っかかるため glob で探す
APP_BUNDLE=""
for candidate in "$BUILD_OUTPUT_DIR"/*.app; do
	[ -d "$candidate" ] || continue
	APP_BUNDLE="$candidate"
	break
done
[ -n "$APP_BUNDLE" ] || die ".app が見つかりません: $BUILD_OUTPUT_DIR"
log ".app: $APP_BUNDLE"

# 焼き込まれたバージョンが意図と一致しているか確認する。
# ここが食い違うと asc builds wait がビルドを見つけられない。
ACTUAL_VERSION="$("$PLIST_BUDDY" -c "Print :CFBundleShortVersionString" "$APP_BUNDLE/Info.plist")"
ACTUAL_BUILD="$("$PLIST_BUDDY" -c "Print :CFBundleVersion" "$APP_BUNDLE/Info.plist")"
[ "$ACTUAL_VERSION" = "$APP_VERSION" ] || die "CFBundleShortVersionString が一致しません: $ACTUAL_VERSION (期待 $APP_VERSION)"
[ "$ACTUAL_BUILD" = "$BUILD_NUMBER" ] || die "CFBundleVersion が一致しません: $ACTUAL_BUILD (期待 $BUILD_NUMBER)"

# ---------------------------------------------------------------------------
# 2. .xcarchive の組み立て
# ---------------------------------------------------------------------------
#
# .xcarchive は Products/Applications/<App>.app と ApplicationProperties を持つ
# Info.plist からなるディレクトリ。xcodebuild -exportArchive はこれを入力に取り、
# 中の .app を改めて署名し直して ipa を書き出す。

ARCHIVE_PATH="$OUTPUT_DIR/KyoshinEewViewer.xcarchive"
log ".xcarchive を組み立てます: $ARCHIVE_PATH"

mkdir -p "$ARCHIVE_PATH/Products/Applications" "$ARCHIVE_PATH/dSYMs"
cp -R "$APP_BUNDLE" "$ARCHIVE_PATH/Products/Applications/"

# dSYM を同梱しておくと ASC 側でシンボリケートされたクラッシュログが読める
# (ExportOptions.plist の uploadSymbols=true と対応)
while IFS= read -r -d '' dsym; do
	cp -R "$dsym" "$ARCHIVE_PATH/dSYMs/"
done < <(find "$BUILD_OUTPUT_DIR" -maxdepth 1 -type d -name '*.dSYM' -print0)

ARCHIVE_INFO_PLIST="$ARCHIVE_PATH/Info.plist"
cat >"$ARCHIVE_INFO_PLIST" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>ApplicationProperties</key>
	<dict>
		<key>ApplicationPath</key>
		<string>Applications/$(basename "$APP_BUNDLE")</string>
		<key>CFBundleIdentifier</key>
		<string>$BUNDLE_ID</string>
		<key>CFBundleShortVersionString</key>
		<string>$APP_VERSION</string>
		<key>CFBundleVersion</key>
		<string>$BUILD_NUMBER</string>
		<key>SigningIdentity</key>
		<string>Apple Distribution</string>
		<key>Team</key>
		<string>$TEAM_ID</string>
	</dict>
	<key>ArchiveVersion</key>
	<integer>2</integer>
	<key>Name</key>
	<string>KyoshinEewViewer</string>
	<key>SchemeName</key>
	<string>KyoshinEewViewer</string>
</dict>
</plist>
PLIST

plutil -lint "$ARCHIVE_INFO_PLIST" >/dev/null || die ".xcarchive の Info.plist が不正です"

# ---------------------------------------------------------------------------
# 3. エクスポート (クラウド署名)
# ---------------------------------------------------------------------------
#
# -allowProvisioningUpdates と ASC API キーを渡すと、証明書とプロビジョニング
# プロファイルが未発行でも Apple 側で自動発行される。
# ローカルにも CI にも .p12 やプロファイルを置かずに済む。

EXPORT_DIR="$OUTPUT_DIR/export"
log "ipa をエクスポートします (クラウド署名)"
mkdir -p "$EXPORT_DIR"

run_xcodebuild -exportArchive \
	-archivePath "$ARCHIVE_PATH" \
	-exportOptionsPlist "$EXPORT_OPTIONS_PLIST" \
	-exportPath "$EXPORT_DIR" \
	-allowProvisioningUpdates \
	-authenticationKeyIssuerID "$ASC_ISSUER_ID" \
	-authenticationKeyID "$ASC_KEY_ID" \
	-authenticationKeyPath "$PRIVATE_KEY_PATH"

IPA_PATH=""
for candidate in "$EXPORT_DIR"/*.ipa; do
	[ -f "$candidate" ] || continue
	IPA_PATH="$candidate"
	break
done
[ -n "$IPA_PATH" ] || die "ipa が見つかりません: $EXPORT_DIR"
log "ipa: $IPA_PATH"

# ---------------------------------------------------------------------------
# 4. アップロード
# ---------------------------------------------------------------------------

if [ "$SKIP_UPLOAD" = "true" ]; then
	log "SKIP_UPLOAD=true のためアップロードをスキップしました: $IPA_PATH"
	exit 0
fi

if [ -n "$TESTFLIGHT_GROUP" ]; then
	log "TestFlight へ配信します (グループ: $TESTFLIGHT_GROUP)"
	# 外部テスターグループはベータ審査が必要なため --submit / --confirm を付ける
	"$ASC" publish testflight \
		--app "$APP_ID" \
		--ipa "$IPA_PATH" \
		--group "$TESTFLIGHT_GROUP" \
		--wait \
		--submit \
		--confirm \
		--timeout 30m
else
	log "App Store Connect へアップロードします"
	"$ASC" builds upload \
		--app "$APP_ID" \
		--ipa "$IPA_PATH" \
		--version "$APP_VERSION" \
		--build-number "$BUILD_NUMBER" \
		--output json

	log "ビルドの処理完了を待ちます (最大30分)"
	"$ASC" builds wait \
		--app "$APP_ID" \
		--version "$APP_VERSION" \
		--build-number "$BUILD_NUMBER" \
		--platform IOS \
		--timeout 30m \
		--fail-on-invalid
fi

log "完了しました: バージョン $APP_VERSION / ビルド $BUILD_NUMBER"
