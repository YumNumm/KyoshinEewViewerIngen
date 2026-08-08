---
name: upstream-sync

on:
  schedule:
    - cron: "0 19 * * *"
  workflow_dispatch:

permissions:
  contents: read
  issues: read
  pull-requests: read

engine: claude
max-turns: 20
timeout-minutes: 45
strict: true

concurrency:
  group: upstream-sync
  cancel-in-progress: false
  queue: max

network:
  allowed:
    - defaults
    - github
    - dotnet

checkout:
  ref: develop
  fetch-depth: 0
  fetch: ["*", "refs/pulls/open/*"]
  submodules: recursive
  force-clean-git-credentials: true

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
    steps:
      - name: Checkout origin/develop
        uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
        with:
          ref: develop
          fetch-depth: 0
          token: ${{ secrets.UPSTREAM_SYNC_TOKEN }}
          persist-credentials: true

      - name: Validate sync token
        env:
          SYNC_TOKEN: ${{ secrets.UPSTREAM_SYNC_TOKEN }}
        run: |
          if [[ -z "$SYNC_TOKEN" ]]; then
            echo "::error::repository secret UPSTREAM_SYNC_TOKEN が必要です。"
            exit 1
          fi

      - name: Find existing conflict pull request
        id: find_pr
        env:
          GH_TOKEN: ${{ secrets.UPSTREAM_SYNC_TOKEN }}
        run: |
          number=$(gh pr list \
            --repo YumNumm/KyoshinEewViewerIngen \
            --state open \
            --base develop \
            --head agentic/upstream-sync \
            --json number,title \
            --jq '[.[] | select(.title | startswith("[upstream-sync] "))][0].number // ""')
          echo "number=$number" >> "$GITHUB_OUTPUT"

      - name: Merge upstream when conflict-free
        id: sync
        env:
          SYNC_ALLOW_WORKTREE_RESET: "true"
          SYNC_UPSTREAM_URL: https://github.com/ingen084/KyoshinEewViewerIngen.git
          SYNC_ORIGIN_REMOTE: origin
          SYNC_ORIGIN_BRANCH: develop
          SYNC_UPSTREAM_REMOTE: upstream
          SYNC_UPSTREAM_BRANCH: develop
          SYNC_PUSH: "true"
        run: |
          SYNC_OUTPUT_FILE="$GITHUB_OUTPUT" bash scripts/upstream-sync.sh

      - name: Close superseded conflict pull request
        if: steps.sync.outputs.status == 'merged' && steps.find_pr.outputs.number != ''
        env:
          GH_TOKEN: ${{ secrets.UPSTREAM_SYNC_TOKEN }}
          PR_NUMBER: ${{ steps.find_pr.outputs.number }}
          MERGE_SHA: ${{ steps.sync.outputs.merge_sha }}
        run: |
          body=$(printf 'upstreamの最新変更を競合なしで `develop` へ直接同期しました（merge commit: `%s`）。このPRは不要になったためcloseします。\n\n@YumNumm' "$MERGE_SHA")
          gh pr comment "$PR_NUMBER" \
            --repo YumNumm/KyoshinEewViewerIngen \
            --body "$body"
          gh pr close "$PR_NUMBER" \
            --repo YumNumm/KyoshinEewViewerIngen

steps:
  - name: Skip AI or prepare conflict refs
    env:
      SYNC_STATUS: ${{ needs.sync.outputs.status }}
    run: |
      if [[ "$SYNC_STATUS" != "conflict" ]]; then
        jq -cn \
          --arg message "upstream同期は $SYNC_STATUS で完了したためAI推論は不要です。" \
          '{type:"noop", message:$message}' >> "$GH_AW_SAFE_OUTPUTS"
        exit 0
      fi

      if git remote get-url upstream >/dev/null 2>&1; then
        git remote set-url upstream https://github.com/ingen084/KyoshinEewViewerIngen.git
      else
        git remote add upstream https://github.com/ingen084/KyoshinEewViewerIngen.git
      fi
      git config --unset-all remote.upstream.pushurl >/dev/null 2>&1 || true
      git config --add remote.upstream.pushurl disabled://upstream
      git fetch --no-tags upstream \
        +refs/heads/develop:refs/remotes/upstream/develop

safe-outputs:
  mentions:
    allowed: [YumNumm]
    allowed-collaborators: false
    allow-context: false
    max: 1

  create-pull-request:
    base-branch: develop
    title-prefix: "[upstream-sync] "
    reviewers: [YumNumm]
    allowed-branches: [agentic/upstream-sync]
    preserve-branch-name: true
    recreate-ref: true
    fallback-as-issue: false
    signed-commits: false

  push-to-pull-request-branch:
    target: "*"
    required-title-prefix: "[upstream-sync] "
    signed-commits: false
    fallback-as-pull-request: false

  add-comment:
    target: "*"
    required-title-prefix: "[upstream-sync] "

  add-reviewer:
    target: "*"
    required-title-prefix: "[upstream-sync] "
    allowed-reviewers: [YumNumm]
    max: 1
---

# upstream/develop の競合解消

決定的同期jobが、`YumNumm/KyoshinEewViewerIngen` の `develop` と
`ingen084/KyoshinEewViewerIngen` の `develop` のmerge競合を検出しました。

## 入力

- origin SHA: `${{ needs.sync.outputs.origin_sha }}`
- upstream SHA: `${{ needs.sync.outputs.upstream_sha }}`
- 競合ファイル:

```text
${{ needs.sync.outputs.conflict_files }}
```

- 既存の同期PR番号: `${{ needs.sync.outputs.pull_request_number }}`

## 絶対条件

- GitHub上の書き込み先は `YumNumm/KyoshinEewViewerIngen` だけです。
- `ingen084/KyoshinEewViewerIngen` へのpush、Pull Request、Issue、コメントを絶対に行わないでください。
- `upstream` remoteはfetch専用です。push URLを変更しないでください。
- origin固有の変更やupstreamの変更を削除しないでください。
- `ours` / `theirs` で全体を一括採用せず、競合箇所ごとに意図を確認してください。
- 競合解消と、それに直接起因するbuild error以外は修正しないでください。
- `AGENTS.md` と `CLAUDE.md` に従ってください。
- merge commitを保持してください。rebaseやsquashへ置き換えないでください。

## 作業手順

既存の同期PR番号が空の場合:

1. `origin/develop` からlocal branch `agentic/upstream-sync` を作成します。
2. `upstream/develop` を `--no-ff` でmergeします。
3. 競合を解消し、merge commitを完成させます。

既存の同期PR番号がある場合:

1. `origin/agentic/upstream-sync` から同名のlocal branchを作成します。
2. 最新の `origin/develop` をmergeして、必要なら競合を解消します。
3. 最新の `upstream/develop` を `--no-ff` でmergeして、競合を解消します。
4. 既存PR branchの履歴を祖先として保持し、fast-forward可能な更新にします。

競合解消後、次を実行してください。

```bash
dotnet build src/KyoshinEewViewer/KyoshinEewViewer.csproj
```

解消によって生じたbuild errorは修正してください。upstream自体に由来する失敗や原因を断定できない失敗は、無関係な修正を加えず結果へ明記してください。

## safe output

既存の同期PR番号が空の場合:

- `create_pull_request` を1回使います。
- branchは必ず `agentic/upstream-sync` にします。
- titleにはprefixを含めず、`upstream/develop を同期する` とします。
- 本文に対象upstreamコミット範囲、競合ファイル、解消概要、build結果を記載します。
- reviewer `YumNumm` はWorkflow側で要求されます。

既存の同期PR番号がある場合:

1. `push_to_pull_request_branch` を使い、そのPR番号へ変更をpushします。
2. push成功後、`add_comment` で次の更新概要を同じPRへ投稿します。
   - 前回から増えたupstreamコミット
   - 変更した競合解消
   - build結果
3. コメントの末尾を `@YumNumm` にします。メンションはこの1回だけです。
4. `add_reviewer` でreviewer `YumNumm` を再要求します。

解消できない場合はcode pushを要求しないでください。既存PRがある場合は、失敗理由と未解消箇所を `add_comment` で通知し、末尾を `@YumNumm` にしてください。既存PRがない場合は `noop` で失敗理由を報告してください。
