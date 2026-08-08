#!/usr/bin/env bash
set -euo pipefail

repository_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
subject="$repository_root/scripts/upstream-sync.sh"
fixture_root=$(mktemp -d)
trap 'rm -rf "$fixture_root"' EXIT

fail() {
	echo "FAIL: $*" >&2
	exit 1
}

assert_equal() {
	local expected=$1
	local actual=$2
	local message=$3
	[[ "$actual" == "$expected" ]] || fail "$message (expected=$expected actual=$actual)"
}

read_output() {
	local key=$1
	local file=$2
	sed -n "s/^${key}=//p" "$file" | tail -1
}

configure_repository() {
	local repository=$1
	git -C "$repository" config user.name "同期テスト"
	git -C "$repository" config user.email "sync-test@example.invalid"
}

commit_file() {
	local repository=$1
	local path=$2
	local content=$3
	local message=$4
	mkdir -p "$(dirname "$repository/$path")"
	printf '%s\n' "$content" > "$repository/$path"
	git -C "$repository" add "$path"
	git -C "$repository" commit -m "$message" >/dev/null
}

create_fixture() {
	local name=$1
	fixture="$fixture_root/$name"
	origin_bare="$fixture/origin.git"
	upstream_bare="$fixture/upstream.git"
	origin_work="$fixture/origin-work"
	upstream_work="$fixture/upstream-work"
	worker="$fixture/worker"
	output_file="$fixture/output"

	mkdir -p "$fixture"
	git init --bare --quiet "$origin_bare"
	git clone --quiet "$origin_bare" "$origin_work" 2>/dev/null
	configure_repository "$origin_work"
	git -C "$origin_work" switch --quiet -c develop
	commit_file "$origin_work" "shared.txt" "base" "base"
	git -C "$origin_work" push --quiet --set-upstream origin develop
	git --git-dir="$origin_bare" symbolic-ref HEAD refs/heads/develop

	git clone --quiet --bare "$origin_bare" "$upstream_bare"
	git --git-dir="$upstream_bare" symbolic-ref HEAD refs/heads/develop
	git clone --quiet "$upstream_bare" "$upstream_work"
	configure_repository "$upstream_work"
}

clone_worker() {
	git clone --quiet "$origin_bare" "$worker"
	configure_repository "$worker"
}

run_subject() {
	local subject_log="$fixture/subject.log"
	if ! (
		cd "$worker"
		SYNC_ALLOW_WORKTREE_RESET=true \
			SYNC_UPSTREAM_URL="$upstream_bare" \
			SYNC_OUTPUT_FILE="$output_file" \
			bash "$subject"
	) > "$subject_log" 2>&1; then
		cat "$subject_log" >&2
		return 1
	fi
}

test_unchanged_when_upstream_is_already_ancestor() {
	create_fixture unchanged
	commit_file "$origin_work" "origin-only.txt" "origin" "origin only"
	git -C "$origin_work" push --quiet
	local before
	before=$(git --git-dir="$origin_bare" rev-parse refs/heads/develop)
	clone_worker

	run_subject

	assert_equal "unchanged" "$(read_output status "$output_file")" "既取込のupstreamは変更なしになること"
	assert_equal "$before" "$(git --git-dir="$origin_bare" rev-parse refs/heads/develop)" "originを更新しないこと"
}

test_clean_divergence_creates_merge_commit() {
	create_fixture clean-merge
	commit_file "$origin_work" "origin-only.txt" "origin" "origin only"
	git -C "$origin_work" push --quiet
	commit_file "$upstream_work" "upstream-only.txt" "upstream" "upstream only"
	git -C "$upstream_work" push --quiet
	local upstream_head
	upstream_head=$(git --git-dir="$upstream_bare" rev-parse refs/heads/develop)
	clone_worker

	run_subject

	local merged_head parent_count
	merged_head=$(git --git-dir="$origin_bare" rev-parse refs/heads/develop)
	parent_count=$(git --git-dir="$origin_bare" cat-file -p "$merged_head" | sed -n 's/^parent //p' | wc -l | tr -d ' ')
	assert_equal "merged" "$(read_output status "$output_file")" "clean mergeを反映すること"
	assert_equal "2" "$parent_count" "merge commitを保持すること"
	git --git-dir="$origin_bare" merge-base --is-ancestor "$upstream_head" "$merged_head" || fail "upstream HEADがoriginの祖先になること"
}

test_conflict_leaves_origin_and_worker_clean() {
	create_fixture conflict
	commit_file "$origin_work" "shared.txt" "origin" "origin conflict"
	git -C "$origin_work" push --quiet
	commit_file "$upstream_work" "shared.txt" "upstream" "upstream conflict"
	git -C "$upstream_work" push --quiet
	local before
	before=$(git --git-dir="$origin_bare" rev-parse refs/heads/develop)
	clone_worker

	run_subject

	assert_equal "conflict" "$(read_output status "$output_file")" "競合を検出すること"
	assert_equal "$before" "$(git --git-dir="$origin_bare" rev-parse refs/heads/develop)" "競合時にoriginを更新しないこと"
	[[ -z $(git -C "$worker" ls-files -u) ]] || fail "競合indexを残さないこと"
	[[ -z $(git -C "$worker" status --porcelain) ]] || fail "競合後のworktreeをcleanに戻すこと"
}

test_push_failure_is_retried_once() {
	create_fixture retry
	commit_file "$upstream_work" "upstream-only.txt" "upstream" "upstream only"
	git -C "$upstream_work" push --quiet
	clone_worker

	cat > "$origin_bare/hooks/pre-receive" <<'HOOK'
#!/bin/sh
marker="$(dirname "$0")/rejected-once"
if [ ! -e "$marker" ]; then
	touch "$marker"
	echo "一度目のpushを拒否します" >&2
	exit 1
fi
exit 0
HOOK
	chmod +x "$origin_bare/hooks/pre-receive"

	run_subject

	assert_equal "merged" "$(read_output status "$output_file")" "push拒否後に再試行すること"
	[[ -f "$origin_bare/hooks/rejected-once" ]] || fail "拒否hookが実行されたこと"
}

test_unchanged_when_upstream_is_already_ancestor
test_clean_divergence_creates_merge_commit
test_conflict_leaves_origin_and_worker_clean
test_push_failure_is_retried_once

echo "upstream-sync tests: PASS"
