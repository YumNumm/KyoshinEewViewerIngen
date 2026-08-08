#!/usr/bin/env bash
#
# TestFlight の「テスト対象」(What to Test) に載せる変更履歴を組み立てるスクリプト。
#
# App Store Connect (以下 ASC) の Build リソースには commit を持たせられる欄が無いため、
# 生成した本文の末尾に `rev: <40桁の commit SHA>` を埋め込んでおき、
# 次回はそれを読み戻して「前回配信した commit」を特定する。
# 時刻ではなく commit を基準にするので、CD が直列化されて連続実行されても取りこぼさない。
#
# 使い方:
#   APP_ID=6797931103 ./scripts/build-testflight-changelog.sh
#
# 出力:
#   $OUTPUT_PATH (既定: out/testflight-changelog.txt) に本文を書き出す。
#   進捗ログは stderr へ流すので、標準出力を汚さない。
#
set -euo pipefail

# ---------------------------------------------------------------------------
# パラメータ (全て環境変数で上書き可能)
# ---------------------------------------------------------------------------

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

APP_ID="${APP_ID:-${ASC_APP_ID:-6797931103}}"

# TestFlight のテスト対象は locale ごとに持つ。rev の読み書きも同じ locale で行う。
LOCALE="${TESTFLIGHT_LOCALE:-ja}"

OUTPUT_PATH="${OUTPUT_PATH:-$REPO_ROOT/out/testflight-changelog.txt}"

# 何件前のビルドまで遡って rev を探すか。
# 手動アップロードのビルドが挟まっても鎖が切れないようにするための保険
LOOKBACK="${LOOKBACK:-5}"

# ASC のテスト対象は 4000 文字まで。超えた分は「その他 N 件」へ寄せる
MAX_LENGTH="${MAX_LENGTH:-4000}"

# 差分の起点。通常は ASC から読み戻すが、鎖が切れたときの手動復旧用に上書きできる
BASE_SHA="${BASE_SHA:-}"

ASC_BIN="${ASC_BIN:-}"

# PR タイトルの取得先。merge commit の本文が空のときだけ gh で引きにいく。
# upstream (ingen084) を絶対に見に行かないよう、常に --repo で明示する
GH_REPO="${GITHUB_REPOSITORY:-}"

# ---------------------------------------------------------------------------
# ユーティリティ
# ---------------------------------------------------------------------------

log() { printf '\033[1;34m==>\033[0m %s\n' "$*" >&2; }
die() {
	printf '\033[1;31mエラー:\033[0m %s\n' "$*" >&2
	exit 1
}

# asc を解決する。mise 経由 / PATH 上 / 明示指定のいずれでも動くようにする。
resolve_asc() {
	if [ -n "$ASC_BIN" ]; then
		echo "$ASC_BIN"
		return
	fi
	if command -v mise >/dev/null 2>&1; then
		local from_mise
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

# origin から owner/repo を組み立てる。SSH 形式と HTTPS 形式のどちらでも動くようにする。
resolve_gh_repo() {
	if [ -n "$GH_REPO" ]; then
		echo "$GH_REPO"
		return
	fi
	local url
	url="$(git -C "$REPO_ROOT" remote get-url origin 2>/dev/null || true)"
	[ -n "$url" ] || return 0
	url="${url%.git}"
	case "$url" in
	*:*/*) echo "${url##*:}" ;;
	*/*/*) echo "$url" | sed -n 's#.*/\([^/]*/[^/]*\)$#\1#p' ;;
	esac
}

# merge commit の本文が空の PR に備えて、GitHub からタイトルを引く。
# gh が無い / 認証が無い場合は空を返し、呼び出し側でブランチ名へ退避する
fetch_pr_title() {
	local number="$1"
	[ -n "$GH_REPO" ] || return 0
	command -v gh >/dev/null 2>&1 || return 0
	gh pr view "$number" --repo "$GH_REPO" --json title -q .title 2>/dev/null || true
}

# asc の認証は呼び出し側に委ねる。ローカルではキーチェーンのプロファイル、
# CI では ASC_BYPASS_KEYCHAIN=1 と環境変数の認証情報が使われる
ASC="$(resolve_asc)"

GH_REPO="$(resolve_gh_repo)"

HEAD_SHA="$(git -C "$REPO_ROOT" rev-parse HEAD)"

# ---------------------------------------------------------------------------
# 1. 前回配信した commit を特定する
# ---------------------------------------------------------------------------
#
# 新しいビルドから順にテスト対象を読み、rev マーカーを持つ最初のビルドを基準にする。
# JSON 中では改行が \n へエスケープされるため、平文のまま grep できる。

if [ -n "$BASE_SHA" ]; then
	git -C "$REPO_ROOT" cat-file -e "$BASE_SHA^{commit}" 2>/dev/null ||
		die "指定された BASE_SHA が手元に存在しません: $BASE_SHA"
	BASE_SHA="$(git -C "$REPO_ROOT" rev-parse "$BASE_SHA")"
	log "BASE_SHA が指定されたため App Store Connect への問い合わせを省略します (rev: $BASE_SHA)"
else
	log "前回配信したビルドを探します (直近 $LOOKBACK 件)"

	BUILDS_JSON=""
	if ! BUILDS_JSON="$(
		"$ASC" builds list \
			--app "$APP_ID" \
			--platform IOS \
			--sort -uploadedDate \
			--limit "$LOOKBACK" \
			--output json
	)"; then
		die "App Store Connect のビルド一覧を取得できませんでした。"
	fi

	# builds list の attributes.version は CFBundleVersion (ビルド番号)
	BUILD_NUMBERS="$(printf '%s' "$BUILDS_JSON" | grep -Eo '"version":"[0-9]+"' | cut -d'"' -f4 || true)"

	for build_number in $BUILD_NUMBERS; do
		notes_json=""
		if ! notes_json="$(
			"$ASC" builds test-notes list \
				--app "$APP_ID" \
				--build-number "$build_number" \
				--platform IOS \
				--locale "$LOCALE" \
				--output json 2>/dev/null
		)"; then
			continue
		fi
		candidate="$(printf '%s' "$notes_json" | grep -Eo 'rev: [0-9a-f]{40}' | tail -1 | cut -d' ' -f2 || true)"
		[ -n "$candidate" ] || continue
		# 別リポジトリや force push 後の残骸を掴まないよう、手元に実在するか確かめる
		if ! git -C "$REPO_ROOT" cat-file -e "$candidate^{commit}" 2>/dev/null; then
			log "ビルド $build_number の rev ($candidate) は手元に存在しないため読み飛ばします"
			continue
		fi
		BASE_SHA="$candidate"
		log "ビルド $build_number を前回の配信とみなします (rev: $BASE_SHA)"
		break
	done

	[ -n "$BASE_SHA" ] || log "rev マーカーを持つビルドが見つかりませんでした"
fi

# ---------------------------------------------------------------------------
# 2. 差分から PR を拾う
# ---------------------------------------------------------------------------

PR_LINES=""
PR_COUNT=0
OTHER_COUNT=0

# 本文の残り 2 要素 (その他 N 件の行と rev 行) の分を確保しておく
BUDGET=$((MAX_LENGTH - 120))
USED=0

if [ -n "$BASE_SHA" ]; then
	# GitHub の merge commit は 1 行目が "Merge pull request #N from ..."、本文が PR タイトル。
	# squash merge の場合は件名が "タイトル (#N)" になる。
	merge_re='^Merge pull request #([0-9]+) from '
	squash_re='^(.+) \(#([0-9]+)\)$'

	while IFS= read -r entry; do
		[ -n "$entry" ] || continue
		sha="${entry%% *}"
		subject="${entry#* }"

		if [[ "$subject" =~ $merge_re ]]; then
			pr_number="${BASH_REMATCH[1]}"
			# merge commit の本文の最初の非空行が PR タイトル。
			# ただし GitHub 上で本文を消してマージすると空になるため、その場合は gh で引き直す
			pr_title="$(git -C "$REPO_ROOT" log -1 --format=%b "$sha" | sed -n '/[^[:space:]]/{p;q;}')"
			[ -n "$pr_title" ] || pr_title="$(fetch_pr_title "$pr_number")"
			# gh も使えないときはブランチ名で代替する
			[ -n "$pr_title" ] || pr_title="${subject#*from }"
		elif [[ "$subject" =~ $squash_re ]]; then
			pr_title="${BASH_REMATCH[1]}"
			pr_number="${BASH_REMATCH[2]}"
		else
			# PR を経ない直接 push や upstream からのマージ。件数だけ数える
			OTHER_COUNT=$((OTHER_COUNT + 1))
			continue
		fi

		line="・#${pr_number} ${pr_title}"
		if [ $((USED + ${#line} + 1)) -gt "$BUDGET" ]; then
			# 文字数上限に達したら残りは件数へ寄せる
			OTHER_COUNT=$((OTHER_COUNT + 1))
			continue
		fi
		PR_LINES="${PR_LINES}${line}"$'\n'
		USED=$((USED + ${#line} + 1))
		PR_COUNT=$((PR_COUNT + 1))
	done < <(git -C "$REPO_ROOT" log --first-parent --format='%H %s' "$BASE_SHA..$HEAD_SHA")
fi

# ---------------------------------------------------------------------------
# 3. 本文を組み立てる
# ---------------------------------------------------------------------------

if [ -z "$BASE_SHA" ]; then
	BODY="前回の配信ビルドを特定できなかったため、変更点の一覧は省略しています。"
elif [ "$PR_COUNT" -eq 0 ] && [ "$OTHER_COUNT" -eq 0 ]; then
	BODY="前回の配信から変更はありません。"
else
	BODY=""
	if [ "$PR_COUNT" -gt 0 ]; then
		BODY="変更点"$'\n'"${PR_LINES}"
	fi
	if [ "$OTHER_COUNT" -gt 0 ]; then
		[ -z "$BODY" ] || BODY="${BODY}"$'\n'
		BODY="${BODY}その他 ${OTHER_COUNT} 件の変更を含みます。"$'\n'
	fi
	# 末尾の改行は下でまとめて付け直す
	BODY="${BODY%$'\n'}"
fi

mkdir -p "$(dirname "$OUTPUT_PATH")"
printf '%s\n\nrev: %s\n' "$BODY" "$HEAD_SHA" >"$OUTPUT_PATH"

log "変更履歴を書き出しました: $OUTPUT_PATH (PR $PR_COUNT 件 / その他 $OTHER_COUNT 件)"
cat "$OUTPUT_PATH" >&2
