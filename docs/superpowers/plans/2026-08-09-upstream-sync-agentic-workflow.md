# Upstream Sync Agentic Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** upstreamの `develop` を毎日同期し、競合なしでは `origin/develop` へ直接merge、競合時だけClaudeが同じPull Requestを作成・更新するAgentic Workflowを導入する。

**Architecture:** Git操作だけを担当する決定的なBashスクリプトを一時Git repositoryでテストし、gh-awのカスタムjobから実行する。同期jobはrepository限定PATでclean mergeをpushし、競合時だけread-onlyのClaude agentへ状態を渡す。Claudeの変更はgh-aw safe outputsで固定branchのPRへ反映する。

**Tech Stack:** Bash、Git、GitHub Actions、GitHub Agentic Workflows `gh-aw v0.84.3`、Claude API、GitHub CLI

## Global Constraints

- upstreamは `ingen084/KyoshinEewViewerIngen`、読み取りbranchは `develop` に固定する。
- 書き込み先は `YumNumm/KyoshinEewViewerIngen` の `origin/develop` と `agentic/upstream-sync` branchだけに固定する。
- upstreamへのpush、Pull Request、Issue、コメントは禁止する。
- clean mergeは毎日04:00 JSTと手動実行で処理し、既存CDを通常起動する。
- Claudeは競合時だけ起動し、20ターン、45分を上限にする。
- 競合PRは `[upstream-sync] ` prefix、base `develop`、reviewer `YumNumm` とする。
- 更新時は概要を同じPRへコメントし、`@YumNumm` をメンションする。
- `GITHUB_TOKEN` はread-onlyとし、直接pushにはrepository限定fine-grained PAT `UPSTREAM_SYNC_TOKEN` を使う。
- Claude認証にはrepository secret `ANTHROPIC_API_KEY` を使う。
- `strict: true`、SHA pin、safe outputsのprotected-files reviewを維持する。
- 既存アプリケーションテストのbaseline失敗は本変更の対象外とする。

---

### Task 1: 決定的Git同期スクリプト

**Files:**
- Create: `scripts/upstream-sync.sh`
- Create: `scripts/tests/upstream-sync-test.sh`

**Interfaces:**
- Consumes environment variables:
  - `SYNC_UPSTREAM_URL` (required)
  - `SYNC_ORIGIN_REMOTE` (default `origin`)
  - `SYNC_ORIGIN_BRANCH` (default `develop`)
  - `SYNC_UPSTREAM_REMOTE` (default `upstream`)
  - `SYNC_UPSTREAM_BRANCH` (default `develop`)
  - `SYNC_OUTPUT_FILE` (required; GitHub output形式)
  - `SYNC_PUSH` (`true` or `false`, default `true`)
  - `SYNC_ALLOW_WORKTREE_RESET` (must equal `true`)
- Produces outputs in `SYNC_OUTPUT_FILE`:
  - `status=unchanged|merged|conflict`
  - `origin_sha=<40 hex SHA>`
  - `upstream_sha=<40 hex SHA>`
  - `merge_sha=<40 hex SHA or empty>`
  - `conflict_files=<newline separated paths>`
- Exit status `0`: one of the three declared states was reached safely.
- Non-zero: fetch, merge cleanup, or push failed without a safe state.

- [ ] **Step 1: Write the isolated Git test harness**

Create `scripts/tests/upstream-sync-test.sh` with `set -euo pipefail`, a `mktemp -d` fixture root, cleanup trap, bare `origin.git` and `upstream.git`, and helpers:

```bash
commit_file() {
  local repo=$1 path=$2 content=$3 message=$4
  mkdir -p "$(dirname "$repo/$path")"
  printf '%s\n' "$content" > "$repo/$path"
  git -C "$repo" add "$path"
  git -C "$repo" commit -m "$message" >/dev/null
}

read_output() {
  local key=$1 file=$2
  sed -n "s/^${key}=//p" "$file" | tail -1
}
```

Add four cases that each invoke `scripts/upstream-sync.sh` from a fresh clone:

1. upstream HEAD is already an ancestor of origin -> `status=unchanged`, origin ref unchanged.
2. upstream and origin diverge without file conflict -> `status=merged`, origin HEAD is a two-parent merge commit, upstream HEAD is an ancestor.
3. both sides change the same line -> `status=conflict`, no unmerged entries remain, origin ref unchanged.
4. origin rejects the first push through a one-shot bare-repository hook -> the script retries and ends in `status=merged`.

- [ ] **Step 2: Run the harness and verify it fails before implementation**

Run:

```bash
bash scripts/tests/upstream-sync-test.sh
```

Expected: FAIL because `scripts/upstream-sync.sh` does not exist.

- [ ] **Step 3: Implement the guarded merge state machine**

Create `scripts/upstream-sync.sh` with these concrete rules:

```bash
#!/usr/bin/env bash
set -euo pipefail

[[ ${SYNC_ALLOW_WORKTREE_RESET:-} == true ]] || {
  echo 'SYNC_ALLOW_WORKTREE_RESET=true が必要です。' >&2
  exit 2
}
: "${SYNC_UPSTREAM_URL:?SYNC_UPSTREAM_URL が必要です。}"
: "${SYNC_OUTPUT_FILE:?SYNC_OUTPUT_FILE が必要です。}"

origin_remote=${SYNC_ORIGIN_REMOTE:-origin}
origin_branch=${SYNC_ORIGIN_BRANCH:-develop}
upstream_remote=${SYNC_UPSTREAM_REMOTE:-upstream}
upstream_branch=${SYNC_UPSTREAM_BRANCH:-develop}
push_enabled=${SYNC_PUSH:-true}
```

The implementation must:

- add or update the fetch URL for `upstream_remote`;
- set its push URL to `disabled://upstream`;
- fetch only the declared upstream branch and fetch the latest origin branch before each attempt;
- use `git checkout --detach --force "$origin_remote/$origin_branch"` in the ephemeral checkout;
- treat `git merge-base --is-ancestor "$upstream_sha" "$origin_sha"` as `unchanged`;
- run `git merge --no-ff --no-edit "$upstream_remote/$upstream_branch"` with Japanese bot identity;
- detect conflicts from `git ls-files -u`, record unique paths, then `git merge --abort`;
- push `HEAD:refs/heads/$origin_branch` only to `origin_remote`;
- retry the complete origin refresh and merge once after a rejected push;
- never use force push;
- write all declared outputs, including empty values, before success exit.

- [ ] **Step 4: Run the focused tests**

Run:

```bash
bash scripts/tests/upstream-sync-test.sh
```

Expected: four cases PASS and final line `upstream-sync tests: PASS`.

- [ ] **Step 5: Run shell syntax and diff checks**

Run:

```bash
bash -n scripts/upstream-sync.sh scripts/tests/upstream-sync-test.sh
git diff --check
```

Expected: exit 0 with no output.

- [ ] **Step 6: Commit the deterministic component**

```bash
git add scripts/upstream-sync.sh scripts/tests/upstream-sync-test.sh
git commit -m "ci: upstream同期スクリプトを追加する"
```

### Task 2: Agentic Workflow source

**Files:**
- Create: `.github/workflows/upstream-sync.md`
- Create: `scripts/tests/upstream-sync-workflow-test.sh`
- Modify: `docs/superpowers/specs/2026-08-09-upstream-sync-agentic-workflow-design.md` only if compiler-confirmed semantics differ from the design

**Interfaces:**
- Consumes Task 1 script and repository secrets `UPSTREAM_SYNC_TOKEN`, `ANTHROPIC_API_KEY`.
- Custom job `sync` produces `status`, `origin_sha`, `upstream_sha`, `merge_sha`, `conflict_files`, `pull_request_number`.
- Agent consumes `${{ needs.sync.outputs.* }}` and emits exactly one code safe output (`create-pull-request` or `push-to-pull-request-branch`) only on conflict.
- Existing PR updates also emit `add-comment` and `add-reviewer`; first creation uses static `reviewers: [YumNumm]`.

- [ ] **Step 1: Write the workflow contract test**

Create `scripts/tests/upstream-sync-workflow-test.sh` with `set -euo pipefail`. It must fail unless `.github/workflows/upstream-sync.md` exists and contains every fixed safety contract:

```bash
workflow=.github/workflows/upstream-sync.md
[[ -f $workflow ]]
rg -q 'engine: claude' "$workflow"
rg -q 'strict: true' "$workflow"
rg -q 'https://github.com/ingen084/KyoshinEewViewerIngen.git' "$workflow"
rg -q 'YumNumm/KyoshinEewViewerIngen' "$workflow"
rg -q 'agentic/upstream-sync' "$workflow"
rg -q 'reviewers: \[YumNumm\]' "$workflow"
rg -q 'allowed-reviewers: \[YumNumm\]' "$workflow"
! rg -q 'target-repo:|head-repo:|allowed-repos:|git push upstream' "$workflow"
```

It must finish by running `gh aw validate upstream-sync --strict`.

Run:

```bash
bash scripts/tests/upstream-sync-workflow-test.sh
```

Expected: FAIL because `.github/workflows/upstream-sync.md` does not exist.

- [ ] **Step 2: Add the deterministic sync job**

Add a `jobs.sync` custom job with:

```yaml
jobs:
  sync:
    runs-on: ubuntu-24.04
    permissions:
      contents: read
      pull-requests: read
    outputs:
      status: ${{ steps.sync.outputs.status }}
      origin_sha: ${{ steps.sync.outputs.origin_sha }}
      upstream_sha: ${{ steps.sync.outputs.upstream_sha }}
      merge_sha: ${{ steps.sync.outputs.merge_sha }}
      conflict_files: ${{ steps.sync.outputs.conflict_files }}
      pull_request_number: ${{ steps.find_pr.outputs.number }}
```

Pin `actions/checkout` to its full commit SHA. Checkout `develop` with full history, the PAT, and persisted credentials only in this deterministic job. Validate `UPSTREAM_SYNC_TOKEN` before running Task 1 script. Query the existing PR with an explicit repository:

```bash
gh pr list \
  --repo YumNumm/KyoshinEewViewerIngen \
  --state open \
  --base develop \
  --head agentic/upstream-sync \
  --json number,title \
  --jq '[.[] | select(.title | startswith("[upstream-sync] "))][0].number // ""'
```

If `status=merged` and a PR exists, comment that clean sync superseded it, mention `@YumNumm`, then close it. Use only `UPSTREAM_SYNC_TOKEN` for these deterministic writes.

- [ ] **Step 3: Add the no-op gate and conflict checkout preparation**

Configure agent checkout with `ref: develop`, `fetch-depth: 0`, `fetch: ["*", "refs/pulls/open/*"]`, recursive submodules, and forced credential cleanup.

Add a top-level pre-agent `steps:` entry that:

- writes a `noop` JSON line to `$GH_AW_SAFE_OUTPUTS` unless `${{ needs.sync.outputs.status }}` equals `conflict`;
- on conflict, adds the public upstream fetch URL, sets `disabled://upstream` as push URL, and fetches `upstream/develop`;
- never receives `UPSTREAM_SYNC_TOKEN` or `ANTHROPIC_API_KEY` directly.

- [ ] **Step 4: Configure Claude and constrained safe outputs**

Set:

```yaml
engine: claude
max-turns: 20
timeout-minutes: 45
strict: true
```

Declare these safe outputs:

- `create-pull-request`: base `develop`, title prefix `[upstream-sync] `, reviewers `[YumNumm]`, fixed branch preservation, closed-branch recreation, Issue fallback disabled, unsigned direct git push to preserve merge commits;
- `push-to-pull-request-branch`: target `"*"`, required title prefix `[upstream-sync] `, unsigned push, no fallback PR;
- `add-comment`: target `"*"`, required title prefix `[upstream-sync] `;
- `add-reviewer`: target `"*"`, allowed reviewers `[YumNumm]`, required title prefix `[upstream-sync] `;
- mentions: only `YumNumm`, maximum one unescaped mention.

Keep protected-files policy at `request_review`. Do not configure `target-repo`, `head-repo`, `allowed-repos`, Issue creation, workflow dispatch, or merge-PR safe outputs.

- [ ] **Step 5: Write the complete conflict-resolution prompt**

The prompt must explicitly branch on `pull_request_number`:

- no existing PR: start from `origin/develop`, merge `upstream/develop`, resolve conflicts, build, create branch `agentic/upstream-sync`, then request `create-pull-request`;
- existing PR: start from `origin/agentic/upstream-sync`, merge `origin/develop`, then `upstream/develop`, resolve conflicts, build, request `push-to-pull-request-branch`, comment summary ending in `@YumNumm`, and add reviewer `YumNumm`;
- if resolution cannot be completed: do not emit a code push; for an existing PR only, comment the failure summary and mention `@YumNumm`;
- never modify or write to `ingen084/KyoshinEewViewerIngen` through GitHub tools;
- do not remove origin-only changes or make unrelated fixes;
- include upstream range, conflict files, resolution, and build result in PR text.

- [ ] **Step 6: Validate the source**

Run:

```bash
bash scripts/tests/upstream-sync-workflow-test.sh
```

Expected: exit 0; no unpinned-action, write-permission, wildcard target, or unsafe secret warnings.

- [ ] **Step 7: Commit the source workflow**

```bash
git add .github/workflows/upstream-sync.md scripts/tests/upstream-sync-workflow-test.sh docs/superpowers/specs/2026-08-09-upstream-sync-agentic-workflow-design.md
git commit -m "ci: upstream同期Agentic Workflowを追加する"
```

### Task 3: Compile and audit the generated workflow

**Files:**
- Create: `.github/workflows/upstream-sync.lock.yml`
- Create or Modify: gh-aw compiler-managed manifest files only when generated by `gh aw compile`

**Interfaces:**
- Consumes `.github/workflows/upstream-sync.md`.
- Produces the immutable GitHub Actions workflow executed on GitHub.

- [ ] **Step 1: Compile with all relevant validators**

Run:

```bash
gh aw compile upstream-sync --validate --actionlint --approve
```

Expected: exit 0 and `.github/workflows/upstream-sync.lock.yml` generated with SHA-pinned gh-aw actions.

- [ ] **Step 2: Re-run strict source validation and focused tests**

Run:

```bash
bash scripts/tests/upstream-sync-test.sh
bash scripts/tests/upstream-sync-workflow-test.sh
git diff --check
```

Expected: all exit 0.

- [ ] **Step 3: Audit permissions, secrets, and destinations**

Run searches that must prove:

```bash
rg -n "ANTHROPIC_API_KEY|UPSTREAM_SYNC_TOKEN|permissions:|contents: write|pull-requests: write|issues: write" \
  .github/workflows/upstream-sync.md .github/workflows/upstream-sync.lock.yml

rg -n "ingen084/KyoshinEewViewerIngen|YumNumm/KyoshinEewViewerIngen|agentic/upstream-sync|refs/heads/develop" \
  scripts/upstream-sync.sh .github/workflows/upstream-sync.md .github/workflows/upstream-sync.lock.yml

! rg -n "gh (pr|issue).*(ingen084|upstream)|git push upstream|target-repo:|head-repo:|allowed-repos:" \
  scripts/upstream-sync.sh .github/workflows/upstream-sync.md
```

Inspect the generated job boundaries and confirm `UPSTREAM_SYNC_TOKEN` appears only in the deterministic sync job, while write permissions generated for PR operations appear only in safe-output jobs.

- [ ] **Step 4: Commit generated artifacts**

```bash
git add .github/workflows/upstream-sync.lock.yml
git status --short
git commit -m "ci: upstream同期Workflowをコンパイルする"
```

If `gh aw compile` changed an existing compiler-managed manifest, inspect that diff and add its exact path before committing. Do not create compiler-managed files manually.

### Task 4: Completion audit and Pull Request delivery

**Files:**
- Verify all files from Tasks 1-3

**Interfaces:**
- Produces branch `feat/agentic-upstream-sync` on `origin` and a Pull Request targeting `YumNumm/KyoshinEewViewerIngen:develop`.

- [ ] **Step 1: Rebase onto the latest origin/develop if needed**

```bash
git fetch origin develop
git rebase origin/develop
```

Resolve only feature-branch conflicts. Re-run Task 3 validation after any rebase.

- [ ] **Step 2: Run the final requirement-by-requirement verification**

```bash
bash scripts/tests/upstream-sync-test.sh
bash -n scripts/upstream-sync.sh scripts/tests/upstream-sync-test.sh scripts/tests/upstream-sync-workflow-test.sh
bash scripts/tests/upstream-sync-workflow-test.sh
gh aw compile upstream-sync --validate --actionlint --approve
git diff --check origin/develop...HEAD
git status --short
```

Expected: focused tests and validators pass; compilation is idempotent; only intended files differ.

- [ ] **Step 3: Verify repository secrets by name without reading values**

```bash
gh secret list --repo YumNumm/KyoshinEewViewerIngen
```

Record whether `ANTHROPIC_API_KEY` and `UPSTREAM_SYNC_TOKEN` still need to be configured after merge. Do not attempt to invent, print, or transfer secret values.

- [ ] **Step 4: Push only to origin**

```bash
git push --set-upstream origin feat/agentic-upstream-sync
```

Do not push to `upstream`.

- [ ] **Step 5: Create the Pull Request with an explicit repository**

```bash
gh pr create \
  --repo YumNumm/KyoshinEewViewerIngen \
  --base develop \
  --head feat/agentic-upstream-sync \
  --title "ci: upstream同期Agentic Workflowを導入する" \
  --body-file /tmp/upstream-sync-pr-body.md
```

The PR body must summarize behavior, security boundaries, tests, baseline failures, and the two post-merge secrets. Never create or comment on an upstream PR or Issue.
