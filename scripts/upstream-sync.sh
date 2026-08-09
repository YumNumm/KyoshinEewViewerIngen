#!/usr/bin/env bash
set -euo pipefail

if [[ ${SYNC_ALLOW_WORKTREE_RESET:-} != true ]]; then
	echo "SYNC_ALLOW_WORKTREE_RESET=true が必要です。" >&2
	exit 2
fi
: "${SYNC_UPSTREAM_URL:?SYNC_UPSTREAM_URL が必要です。}"
: "${SYNC_OUTPUT_FILE:?SYNC_OUTPUT_FILE が必要です。}"

origin_remote=${SYNC_ORIGIN_REMOTE:-origin}
origin_branch=${SYNC_ORIGIN_BRANCH:-develop}
upstream_remote=${SYNC_UPSTREAM_REMOTE:-upstream}
upstream_branch=${SYNC_UPSTREAM_BRANCH:-develop}
push_enabled=${SYNC_PUSH:-true}

case "$push_enabled" in
true | false) ;;
*)
	echo "SYNC_PUSH は true または false で指定してください。" >&2
	exit 2
	;;
esac

status=""
origin_sha=""
upstream_sha=""
merge_sha=""
conflict_files=""

write_outputs() {
	{
		printf 'status=%s\n' "$status"
		printf 'origin_sha=%s\n' "$origin_sha"
		printf 'upstream_sha=%s\n' "$upstream_sha"
		printf 'merge_sha=%s\n' "$merge_sha"
		printf 'conflict_files<<SYNC_CONFLICT_FILES_EOF\n'
		printf '%s\n' "$conflict_files"
		printf 'SYNC_CONFLICT_FILES_EOF\n'
	} > "$SYNC_OUTPUT_FILE"
}

if git remote get-url "$upstream_remote" >/dev/null 2>&1; then
	git remote set-url "$upstream_remote" "$SYNC_UPSTREAM_URL"
else
	git remote add "$upstream_remote" "$SYNC_UPSTREAM_URL"
fi
git config --unset-all "remote.${upstream_remote}.pushurl" >/dev/null 2>&1 || true
git config --add "remote.${upstream_remote}.pushurl" "disabled://upstream"

git fetch --no-tags "$upstream_remote" \
	"+refs/heads/${upstream_branch}:refs/remotes/${upstream_remote}/${upstream_branch}"
upstream_sha=$(git rev-parse "refs/remotes/${upstream_remote}/${upstream_branch}")

attempt=1
while [[ $attempt -le 2 ]]; do
	git fetch --no-tags "$origin_remote" \
		"+refs/heads/${origin_branch}:refs/remotes/${origin_remote}/${origin_branch}"
	origin_sha=$(git rev-parse "refs/remotes/${origin_remote}/${origin_branch}")
	git checkout --detach --force "$origin_sha" >/dev/null 2>&1

	if git merge-base --is-ancestor "$upstream_sha" "$origin_sha"; then
		status="unchanged"
		write_outputs
		exit 0
	fi

	git config user.name "upstream同期Bot"
	git config user.email "41898282+github-actions[bot]@users.noreply.github.com"

	if ! git merge --no-ff --no-edit "$upstream_sha"; then
		conflict_files=$(git diff --name-only --diff-filter=U | LC_ALL=C sort -u)
		if [[ -n "$conflict_files" ]]; then
			git merge --abort
			status="conflict"
			write_outputs
			exit 0
		fi

		git merge --abort >/dev/null 2>&1 || true
		echo "競合ファイルを特定できない理由でmergeに失敗しました。" >&2
		exit 1
	fi

	merge_sha=$(git rev-parse HEAD)
	if [[ $push_enabled == false ]]; then
		status="merged"
		write_outputs
		exit 0
	fi

	if git push "$origin_remote" "HEAD:refs/heads/${origin_branch}"; then
		status="merged"
		write_outputs
		exit 0
	fi

	if [[ $attempt -eq 2 ]]; then
		echo "origin/${origin_branch} へのpushに2回失敗しました。" >&2
		exit 1
	fi

	echo "pushに失敗したため、最新のoriginから同期を再試行します。" >&2
	attempt=$((attempt + 1))
done
