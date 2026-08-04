#!/usr/bin/env bash
#
# KyoshinEewViewer macOS 版を Mac App Store / TestFlight 向けの署名済み pkg にして
# App Store Connect へアップロードするスクリプト。
#
# ローカルでも GitHub Actions でも同じ手順で動くよう、入力は全て環境変数で受け取る。
# 詳細な前提条件・トラブルシューティングは docs/TESTFLIGHT_MACOS.md を参照。
#
# 使い方:
#   BUILD_NUMBER=2 ./scripts/publish-testflight-macos.sh
#   SKIP_UPLOAD=true ./scripts/publish-testflight-macos.sh   # pkg 作成まででアップロードしない
#
set -euo pipefail

# ---------------------------------------------------------------------------
# パラメータ (全て環境変数で上書き可能。既定値はローカル開発機向け)
# ---------------------------------------------------------------------------

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# App Store Connect 上のアプリ識別子
# net.yumnumm.KyoshinEewViewerIngen 用のアプリレコードは ASC 上に新規作成が必要なため、
# APP_ID に既定値は置かない (旧 net.yumnumm.kevi のアプリ 6517347515 とは Bundle ID が一致しない)
APP_ID="${APP_ID:-}"
BUNDLE_ID="${BUNDLE_ID:-net.yumnumm.KyoshinEewViewerIngen}"
TEAM_ID="${TEAM_ID:-CPL7H8SHVM}"

# バージョン。APP_VERSION は CFBundleShortVersionString、BUILD_NUMBER は CFBundleVersion になる。
# BUILD_NUMBER 未指定時は asc に問い合わせて次に使える番号を取得する。
APP_VERSION="${APP_VERSION:-1.0}"
BUILD_NUMBER="${BUILD_NUMBER:-}"

# ビルド対象。universal 化は将来課題 (docs/TESTFLIGHT_MACOS.md 参照)
RID="${RID:-osx-arm64}"
TFM="${TFM:-net10.0}"

# 署名資材
SIGNING_IDENTITY_APP="${SIGNING_IDENTITY_APP:-3rd Party Mac Developer Application: Ryotaro Onoue (CPL7H8SHVM)}"
SIGNING_IDENTITY_INSTALLER="${SIGNING_IDENTITY_INSTALLER:-3rd Party Mac Developer Installer: Ryotaro Onoue (CPL7H8SHVM)}"
PROVISIONING_PROFILE="${PROVISIONING_PROFILE:-$HOME/.kevi-signing/KEVI_MacAppStore.provisionprofile}"
# codesign が参照するキーチェーン。専用キーチェーンを使う場合に指定する。
SIGNING_KEYCHAIN="${SIGNING_KEYCHAIN:-}"

# 出力先。既定はリポジトリ直下の out/ (git-ignore 済み)
OUTPUT_DIR="${OUTPUT_DIR:-$REPO_ROOT/out/testflight-macos}"
APP_BUNDLE_NAME="${APP_BUNDLE_NAME:-KyoshinEewViewer.app}"

# asc CLI。mise 管理下のバイナリを優先する。
ASC_BIN="${ASC_BIN:-}"

SKIP_UPLOAD="${SKIP_UPLOAD:-false}"

# ---------------------------------------------------------------------------
# ユーティリティ
# ---------------------------------------------------------------------------

log() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m警告:\033[0m %s\n' "$*" >&2; }
die() { printf '\033[1;31mエラー:\033[0m %s\n' "$*" >&2; exit 1; }

PLIST_BUDDY=/usr/libexec/PlistBuddy

# Info.plist などへキーを設定する。既存キーがあれば Set、無ければ Add にフォールバックする。
plist_set() {
	local plist="$1" key="$2" type="$3" value="$4"
	if ! "$PLIST_BUDDY" -c "Set :$key $value" "$plist" >/dev/null 2>&1; then
		"$PLIST_BUDDY" -c "Add :$key $type $value" "$plist" >/dev/null
	fi
}

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

# ---------------------------------------------------------------------------
# 事前チェック
# ---------------------------------------------------------------------------

log "事前チェックを実行します"

[ "$(uname -s)" = "Darwin" ] || die "このスクリプトは macOS 上でのみ実行できます。"
[ -n "$APP_ID" ] || die "APP_ID を指定してください。$BUNDLE_ID のアプリレコードを ASC で作成し、その App ID を渡します (asc apps list で確認できます)。"
[ -f "$PROVISIONING_PROFILE" ] || die "プロビジョニングプロファイルが見つかりません: $PROVISIONING_PROFILE"

TEMPLATE_APP="$REPO_ROOT/build-files/KyoshinEewViewer.Desktop.app"
[ -d "$TEMPLATE_APP" ] || die ".app テンプレートが見つかりません: $TEMPLATE_APP"

# codesign に渡すキーチェーン引数を組み立てる
CODESIGN_KEYCHAIN_ARGS=()
if [ -n "$SIGNING_KEYCHAIN" ]; then
	CODESIGN_KEYCHAIN_ARGS=(--keychain "$SIGNING_KEYCHAIN")
fi

# 署名 identity が実在するか確認しておく (存在しないと publish 後に落ちて時間を無駄にする)
if ! security find-identity -v ${SIGNING_KEYCHAIN:+"$SIGNING_KEYCHAIN"} 2>/dev/null | grep -qF "$SIGNING_IDENTITY_APP"; then
	die ".app 署名用の identity が見つかりません: $SIGNING_IDENTITY_APP"
fi
if ! security find-identity -v ${SIGNING_KEYCHAIN:+"$SIGNING_KEYCHAIN"} 2>/dev/null | grep -qF "$SIGNING_IDENTITY_INSTALLER"; then
	die "pkg 署名用の identity が見つかりません: $SIGNING_IDENTITY_INSTALLER"
fi

ASC="$(resolve_asc)"
log "asc: $ASC"

# ---------------------------------------------------------------------------
# ビルド番号の決定
# ---------------------------------------------------------------------------

if [ -z "$BUILD_NUMBER" ]; then
	log "ビルド番号を App Store Connect から取得します"
	BUILD_NUMBER="$(
		"$ASC" builds next-build-number \
			--app "$APP_ID" \
			--version "$APP_VERSION" \
			--platform MAC_OS \
			--output json |
			sed -n 's/.*"nextBuildNumber":"\([0-9]*\)".*/\1/p'
	)"
	[ -n "$BUILD_NUMBER" ] || die "ビルド番号の取得に失敗しました。BUILD_NUMBER を明示指定してください。"
fi

log "バージョン: $APP_VERSION / ビルド番号: $BUILD_NUMBER / RID: $RID"

# ---------------------------------------------------------------------------
# 1. dotnet publish
# ---------------------------------------------------------------------------
#
# PublishSingleFile は使わない。単一ファイル形式は起動時にネイティブライブラリを
# 一時ディレクトリへ展開するため、App Sandbox と署名検証の両方と相性が悪い。
# App Store 配布ではフォルダ形式で publish し、Contents/MacOS 内の各ファイルを
# 個別に署名する (詳細は署名セクションのコメント参照)。

PUBLISH_DIR="$OUTPUT_DIR/publish"
APP_BUNDLE="$OUTPUT_DIR/$APP_BUNDLE_NAME"
PKG_PATH="$OUTPUT_DIR/KyoshinEewViewer-$APP_VERSION-$BUILD_NUMBER.pkg"

log "publish 先を初期化します: $OUTPUT_DIR"
rm -rf "$OUTPUT_DIR"
mkdir -p "$PUBLISH_DIR"

log "dotnet publish を実行します"
APP_VERSION="$APP_VERSION" BUILD_NUMBER="$BUILD_NUMBER" \
	dotnet publish "$REPO_ROOT/src/KyoshinEewViewer.Desktop/KyoshinEewViewer.Desktop.csproj" \
	-c Release \
	-f "$TFM" \
	-r "$RID" \
	-o "$PUBLISH_DIR" \
	-p:PublishSingleFile=false \
	--self-contained true

# ---------------------------------------------------------------------------
# 2. .app レイアウト
# ---------------------------------------------------------------------------
#
# .github/actions/publish-kevi/action.yml と同じ構成 (publish 出力を丸ごと
# Contents/MacOS へ入れる) に揃える。

log ".app バンドルを構成します: $APP_BUNDLE"
cp -R "$TEMPLATE_APP" "$APP_BUNDLE"
mkdir -p "$APP_BUNDLE/Contents/MacOS"
# publish 出力をそのまま Contents/MacOS へ移す
(cd "$PUBLISH_DIR" && tar cf - .) | (cd "$APP_BUNDLE/Contents/MacOS" && tar xf -)
rm -rf "$PUBLISH_DIR"

[ -f "$APP_BUNDLE/Contents/Resources/icon.icns" ] || die "icon.icns が見つかりません。App Store の検証で弾かれます。"

# ---------------------------------------------------------------------------
# 3. Info.plist の書き換え
# ---------------------------------------------------------------------------
#
# チェックイン済みテンプレート (build-files/...) は ad-hoc 配布と共用しているため
# 変更しない。コピー後のバンドル側だけをここで上書きする。

INFO_PLIST="$APP_BUNDLE/Contents/Info.plist"
log "Info.plist を TestFlight 向けに書き換えます"

# テンプレートは BOM 付き XML なので plutil で正規化してから PlistBuddy を使う
plutil -convert xml1 "$INFO_PLIST"

plist_set "$INFO_PLIST" CFBundleIdentifier string "$BUNDLE_ID"
plist_set "$INFO_PLIST" CFBundleShortVersionString string "$APP_VERSION"
plist_set "$INFO_PLIST" CFBundleVersion string "$BUILD_NUMBER"
# App Store のカテゴリ指定は必須
plist_set "$INFO_PLIST" LSApplicationCategoryType string "public.app-category.weather"
# .NET 10 がターゲットにできる最小の macOS は 12 だが、実際にテストされているのは
# Apple がサポート中のバージョンのみ。2024年にアップロードした build 1 の
# minOsVersion も 13.0 だったため、それに揃える。
plist_set "$INFO_PLIST" LSMinimumSystemVersion string "13.0"
# 標準の暗号のみ利用しているため輸出コンプライアンス質問をスキップする
plist_set "$INFO_PLIST" ITSAppUsesNonExemptEncryption bool false

plutil -lint "$INFO_PLIST" >/dev/null || die "Info.plist が不正です"

# ---------------------------------------------------------------------------
# 4. プロビジョニングプロファイルの埋め込み
# ---------------------------------------------------------------------------

log "プロビジョニングプロファイルを埋め込みます"
cp "$PROVISIONING_PROFILE" "$APP_BUNDLE/Contents/embedded.provisionprofile"

# ---------------------------------------------------------------------------
# 4.5 バンドルの正規化 (署名前に必ず実施する)
# ---------------------------------------------------------------------------
#
# App Store の検証を通すために、署名前にバンドルの中身を整える。
# lipo と chmod は署名を無効化するため、必ず署名より前に行う。

# createdump は .NET のクラッシュダンプ採取用ヘルパーで、メイン実行ファイルとは別の
# Mach-O 実行ファイル。App Store は全ての実行ファイルに app-sandbox entitlement を
# 要求する (ITMS-90296) ため、entitlements を付けずに同梱すると検証で弾かれる。
# アプリの動作には不要 (DOTNET_DbgEnableMiniDump 指定時のみ使われる) なので削除する。
if [ -f "$APP_BUNDLE/Contents/MacOS/createdump" ]; then
	log "createdump を削除します (App Store は全実行ファイルに sandbox entitlement を要求する)"
	rm -f "$APP_BUNDLE/Contents/MacOS/createdump"
fi

# 32bit (i386) スライスを含むバイナリは "Unsupported Architectures" (ITMS-90240) で
# 弾かれる。同梱している libbass.dylib は x86_64 / i386 / arm64 の fat binary なので
# i386 を除去する。
log "32bit (i386) スライスを除去します"
while IFS= read -r -d '' binary; do
	case "$(file -b "$binary")" in
	*Mach-O*) ;;
	*) continue ;;
	esac
	case " $(lipo -archs "$binary" 2>/dev/null) " in
	*" i386 "*)
		log "  i386 を除去: ${binary#"$APP_BUNDLE"/}"
		lipo -remove i386 "$binary" -output "$binary.thinned"
		mv "$binary.thinned" "$binary"
		;;
	esac
done < <(find "$APP_BUNDLE/Contents" -type f -print0)

# root 以外が読めないファイルが含まれると、インストール後に署名検証ができず
# "installer package includes files that are only readable by the root user"
# (ITMS-90255) で弾かれる。プロビジョニングプロファイルは元ファイルが 600 のため
# cp で 600 のまま入る点に注意。
log "パーミッションを正規化します"
find "$APP_BUNDLE" -type d -exec chmod 755 {} +
find "$APP_BUNDLE" -type f -exec chmod 644 {} +
# 実行ファイルと動的ライブラリだけ実行ビットを戻す
while IFS= read -r -d '' binary; do
	case "$(file -b "$binary")" in
	*Mach-O*) chmod 755 "$binary" ;;
	esac
done < <(find "$APP_BUNDLE/Contents" -type f -print0)

# 正規化後の状態を検証する。ここで止めれば 15 分かかるアップロードを無駄にしない。
log "バンドルを検証します"
MAIN_EXECUTABLE_NAME="$("$PLIST_BUDDY" -c "Print :CFBundleExecutable" "$INFO_PLIST")"

# ITMS-90240: 32bit スライスが残っていないか
LEFTOVER_I386=""
# ITMS-90296: メイン実行ファイル以外の Mach-O 実行体が残っていないか
# (残す場合は app-sandbox + com.apple.security.inherit を付けて個別署名が必要)
STRAY_EXECUTABLES=""
while IFS= read -r -d '' binary; do
	FILETYPE="$(file -b "$binary")"
	case "$FILETYPE" in
	*Mach-O*) ;;
	*) continue ;;
	esac
	case " $(lipo -archs "$binary" 2>/dev/null) " in
	*" i386 "*) LEFTOVER_I386="$LEFTOVER_I386
  ${binary#"$APP_BUNDLE"/}" ;;
	esac
	case "$FILETYPE" in
	*"Mach-O"*executable*)
		if [ "$(basename "$binary")" != "$MAIN_EXECUTABLE_NAME" ]; then
			STRAY_EXECUTABLES="$STRAY_EXECUTABLES
  ${binary#"$APP_BUNDLE"/}"
		fi
		;;
	esac
done < <(find "$APP_BUNDLE/Contents" -type f -print0)

[ -z "$LEFTOVER_I386" ] || die "i386 スライスが残っています (ITMS-90240 になります):$LEFTOVER_I386"
[ -z "$STRAY_EXECUTABLES" ] || die "メイン以外の実行ファイルが残っています (ITMS-90296 になります):$STRAY_EXECUTABLES"

UNREADABLE_IN_APP="$(find "$APP_BUNDLE" ! -perm -o+r)"
[ -z "$UNREADABLE_IN_APP" ] || die "root しか読めないファイルがあります (ITMS-90255 になります): $UNREADABLE_IN_APP"

log "検証OK (i386 なし / 余分な実行ファイルなし / root専用ファイルなし)"

# ---------------------------------------------------------------------------
# 5. entitlements の生成
# ---------------------------------------------------------------------------
#
# App Store 配布では App Sandbox が必須。
# allow-jit は .NET ランタイムの JIT、network.client は地震データ取得に必要。
#
# temporary-exception.mach-lookup で launchservicesd を許可しているのは、
# これが無いと起動時に SIGABRT でクラッシュするため (詳細は
# docs/TESTFLIGHT_MACOS.md の「遭遇したエラー 6」)。
# Avalonia が初期化中に [NSApplication sharedApplication] を呼ぶと、AppKit 内部の
# 旧 Carbon API 経由で _RegisterApplication() が走り ASN 取得のために
# com.apple.coreservices.launchservicesd への mach-lookup が必要になる。
# ところが Apple の application.sb は LaunchServices の mach サービスを一切
# 許可していないため sandbox に拒否され abort() する。
# 非サンドボックスの ad-hoc 配布では同じ lookup が通るので発覚しない。

ENTITLEMENTS="$OUTPUT_DIR/kevi.entitlements"
log "entitlements を生成します: $ENTITLEMENTS"
cat >"$ENTITLEMENTS" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>com.apple.application-identifier</key>
	<string>$TEAM_ID.$BUNDLE_ID</string>
	<key>com.apple.developer.team-identifier</key>
	<string>$TEAM_ID</string>
	<key>com.apple.security.app-sandbox</key>
	<true/>
	<key>com.apple.security.cs.allow-jit</key>
	<true/>
	<key>com.apple.security.network.client</key>
	<true/>
	<key>com.apple.security.files.user-selected.read-write</key>
	<true/>
	<key>com.apple.security.temporary-exception.mach-lookup.global-name</key>
	<array>
		<string>com.apple.coreservices.launchservicesd</string>
	</array>
</dict>
</plist>
PLIST

plutil -lint "$ENTITLEMENTS" >/dev/null || die "entitlements が不正です"

# ---------------------------------------------------------------------------
# 6. 署名
# ---------------------------------------------------------------------------
#
# 内側 (ネイティブライブラリ) から外側 (.app 本体) の順に署名する。
# --deep は各バイナリへ適切な entitlements を付けられないため使わない。

log "Contents/MacOS 内のファイルを個別に署名します"
# codesign は Contents/MacOS を「nested code の置き場所」として扱うため、
# メイン実行ファイル以外の全ファイルが独立した署名を要求される。
# .dylib やマネージド .dll だけでなく .deps.json / .runtimeconfig.json のような
# データファイルまで対象で、1 つでも漏れるとバンドル署名が
# "code object is not signed at all" で失敗する。
# そのため種別で絞らず、メイン実行ファイル以外を無条件に署名する。
# 非 Mach-O ファイルの署名は拡張属性 (com.apple.cs.CodeSignature) に格納される。
#
# メイン実行ファイルは除外する。これを個別に codesign へ渡すと codesign が
# バンドル署名へ読み替えてしまい、意図しない挙動になる。
MAIN_EXECUTABLE="$APP_BUNDLE/Contents/MacOS/$MAIN_EXECUTABLE_NAME"
[ -f "$MAIN_EXECUTABLE" ] || die "メイン実行ファイルが見つかりません: $MAIN_EXECUTABLE"

SIGNED_COUNT=0
while IFS= read -r -d '' candidate; do
	if [ "$candidate" = "$MAIN_EXECUTABLE" ]; then
		continue
	fi
	codesign --force --timestamp --options runtime \
		"${CODESIGN_KEYCHAIN_ARGS[@]+"${CODESIGN_KEYCHAIN_ARGS[@]}"}" \
		--sign "$SIGNING_IDENTITY_APP" "$candidate"
	SIGNED_COUNT=$((SIGNED_COUNT + 1))
done < <(find "$APP_BUNDLE/Contents/MacOS" -type f -print0)
log "$SIGNED_COUNT 件を署名しました"

log ".app 本体を署名します"
codesign --force --timestamp --options runtime \
	"${CODESIGN_KEYCHAIN_ARGS[@]+"${CODESIGN_KEYCHAIN_ARGS[@]}"}" \
	--entitlements "$ENTITLEMENTS" \
	--sign "$SIGNING_IDENTITY_APP" "$APP_BUNDLE"

log "署名を検証します"
codesign --verify --strict --verbose=2 "$APP_BUNDLE"
codesign --display --entitlements - --xml "$APP_BUNDLE" >/dev/null

# ---------------------------------------------------------------------------
# 7. pkg 作成
# ---------------------------------------------------------------------------

log "pkg を作成します: $PKG_PATH"
productbuild \
	--component "$APP_BUNDLE" /Applications \
	--sign "$SIGNING_IDENTITY_INSTALLER" \
	${SIGNING_KEYCHAIN:+--keychain "$SIGNING_KEYCHAIN"} \
	"$PKG_PATH"

log "pkg の署名を検証します"
pkgutil --check-signature "$PKG_PATH"

# pkg のペイロードを実際に展開して、root しか読めないファイルが混入していないか確認する。
# .app 側で正規化していても、資材の差し替えなどで再発すると ITMS-90255 で
# アップロード後に落ちる。アップロード前にここで止めたい。
log "pkg ペイロードのパーミッションを検証します"
PKG_EXPAND_DIR="$OUTPUT_DIR/pkg-verify"
rm -rf "$PKG_EXPAND_DIR"
pkgutil --expand-full "$PKG_PATH" "$PKG_EXPAND_DIR" >/dev/null
UNREADABLE="$(find "$PKG_EXPAND_DIR" ! -perm -o+r)"
if [ -n "$UNREADABLE" ]; then
	printf '%s\n' "$UNREADABLE" >&2
	die "pkg 内に root しか読めないファイルがあります (ITMS-90255 になります)"
fi
rm -rf "$PKG_EXPAND_DIR"
log "pkg ペイロードに root 専用ファイルはありません"

# ---------------------------------------------------------------------------
# 8. アップロード
# ---------------------------------------------------------------------------

if [ "$SKIP_UPLOAD" = "true" ]; then
	log "SKIP_UPLOAD=true のためアップロードをスキップしました: $PKG_PATH"
	exit 0
fi

log "App Store Connect へアップロードします"
# --version / --build-number は pkg から自動抽出されるが、取り違えを防ぐため明示する
"$ASC" builds upload \
	--app "$APP_ID" \
	--pkg "$PKG_PATH" \
	--version "$APP_VERSION" \
	--build-number "$BUILD_NUMBER"

log "ビルドの処理完了を待ちます (最大30分)"
# アップロード直後は ASC 側にビルドが現れるまで数分かかることがあるため timeout を長めに取る
"$ASC" builds wait \
	--app "$APP_ID" \
	--version "$APP_VERSION" \
	--build-number "$BUILD_NUMBER" \
	--platform MAC_OS \
	--timeout 30m \
	--fail-on-invalid

log "完了しました: バージョン $APP_VERSION / ビルド $BUILD_NUMBER"
