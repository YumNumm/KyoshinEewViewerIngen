# upstream同期Agentic Workflow 設計

## 目的

`ingen084/KyoshinEewViewerIngen` の `develop` を毎日取得し、`YumNumm/KyoshinEewViewerIngen` の `develop` へ安全に反映する。

- 競合がなければ、AI推論を行わずmerge commitを `origin/develop` へ直接pushする。
- 競合があればClaudeが解消し、`YumNumm/KyoshinEewViewerIngen` 内のPull Requestとして人間のレビューへ渡す。
- upstreamへは、push、Pull Request作成、Issue作成、コメント投稿を一切行わない。

## トリガー

- 毎日 04:00 JST（19:00 UTC）
- `workflow_dispatch` による手動実行
- 同じ同期Workflowの並列実行は禁止し、実行中のrunは途中キャンセルしない。

## コンポーネント

### Agentic Workflowソース

`.github/workflows/upstream-sync.md` を人が編集する唯一のAgentic Workflowソースとし、`gh aw compile` が生成する `.github/workflows/upstream-sync.lock.yml` もコミットする。

Claudeエンジンを使用し、認証情報はGitHub Actions Secret `ANTHROPIC_API_KEY` からgh-awのAPIプロキシへ渡す。モデルはgh-awのClaude既定モデルを使用する。Claudeの実行上限は20ターン、45分とする。

### 決定的同期処理

AI実行前のカスタムjobが次の処理を行う。

1. `origin/develop` を完全履歴でcheckoutする。
2. `https://github.com/ingen084/KyoshinEewViewerIngen.git` をfetch専用の `upstream` として追加する。
3. upstreamのpush URLを無効化する。
4. `upstream/develop` をfetchする。
5. `origin/develop` と同一なら `unchanged` を返す。
6. mergeを試し、競合がなければmerge commitを作成して `origin/develop` へfast-forward pushする。
7. 競合があればmergeをabortし、`conflict`、upstream SHA、既存同期PR番号をagent jobへ渡す。

直接pushがnon-fast-forwardで拒否された場合は、最新の `origin/develop` から一度だけ処理をやり直す。再試行後もpushできない場合は書き込まず失敗する。再試行で競合が発生した場合はClaude経路へ移る。

agent job自体に `needs.sync.outputs.status == 'conflict'` の条件を設定し、`unchanged` または直接push成功時はClaudeを起動しない。

### Claudeによる競合解消

Claudeは読み取り専用のGitHub権限と、workspaceへの書き込み権限だけを持つ。次の手順をプロンプトで要求する。

1. 最新の `origin/develop` と `upstream/develop` を取得する。
2. upstreamをmergeし、競合箇所だけを解消する。
3. upstreamまたはoriginの無関係な変更を巻き戻さない。
4. `AGENTS.md` と `CLAUDE.md` に従う。
5. `dotnet build src/KyoshinEewViewer/KyoshinEewViewer.csproj` を実行する。
6. 解消方針、変更ファイル、検証結果をまとめる。
7. 初回はPRを作成し、既存PRがあれば同じPRブランチを更新する。

ビルド失敗が解消内容によるものかupstream由来か判断できない場合、無関係な修正を加えない。失敗をPR本文または更新コメントへ明記してレビューへ渡す。

## 競合PRの管理

- repository: `YumNumm/KyoshinEewViewerIngen` 固定
- base branch: `develop`
- head branch: `agentic/upstream-sync`
- title prefix: `[upstream-sync] `
- reviewer: `YumNumm`
- Issueへのフォールバック: 無効
- force push: 初回作成時に閉じた古いbranchを再作成する場合を除き禁止

初回競合時は、固定branchからPRを作成し、`YumNumm` をreviewerとして要求する。PR本文には以下を含める。

- 対象upstreamコミット範囲
- 競合したファイル
- 競合解消の概要
- 実行した検証と結果

未マージPRがある場合は、`push-to-pull-request-branch` で同じhead branchを更新する。操作対象はtitle prefixとhead branchで制約する。更新後は次をPRコメントへ投稿し、末尾で `@YumNumm` をメンションする。

- 前回から増えたupstreamコミット
- 変更された競合解消内容
- 実行した検証と結果

`YumNumm` はメンション許可リストへ明示し、更新時にもreviewerとして再要求する。

未マージの競合PRが存在する状態で、後日の同期が競合なしになった場合は `origin/develop` へ直接mergeした後、PRへ理由をコメントしてcloseする。

## 履歴の保持

直接同期と競合解消PRのどちらもmerge commitを保持する。これにより、取り込んだupstreamコミットをGitの祖先関係で追跡し、同じ差分を次回以降に再処理しない。

gh-awのPR安全出力ではmerge commitを保持するため `signed-commits: false` を指定する。安全出力job以外にはPR branchへのpush権限を与えない。

## 権限と安全境界

`GITHUB_TOKEN` で作成したpushイベントは別のGitHub Actions Workflowを起動しないため、決定的jobはrepository secret `UPSTREAM_SYNC_TOKEN` に保存したfine-grained PATを使用する。このPATは `YumNumm/KyoshinEewViewerIngen` だけを対象とし、Contents、Pull requests、Issuesのread/write権限だけを持たせる。これにより直接同期後の既存CDを通常どおり起動する。

Workflowの `GITHUB_TOKEN` はread-onlyのままとし、`strict: true` を維持する。権限はjob単位で分離する。

- 決定的同期jobの `GITHUB_TOKEN`: `contents: read`、`pull-requests: read`
- 決定的同期jobの `UPSTREAM_SYNC_TOKEN`: このrepository限定のContents、Pull requests、Issues read/write
- agent job: `contents: read`、`pull-requests: read`、`issues: read`
- safe outputs job: PR作成・更新・コメント・reviewer要求に必要な書き込み権限
- upstream remote: 認証情報なし、push URL無効

トップレベルの書き込み先、PRの対象repository、base branchを `YumNumm/KyoshinEewViewerIngen` と `develop` に固定する。cross-repository safe outputは設定しない。

Agentic Workflowソース自身、`AGENTS.md`、`CLAUDE.md`、`.github/`、依存マニフェストなどのprotected filesをClaudeが変更した場合、gh-aw既定のreview要求を維持し、人間の承認なしにmergeしない。

`UPSTREAM_SYNC_TOKEN` は決定的同期jobにだけ渡し、agent job、safe outputs、submodule、upstream remoteには渡さない。決定的同期jobはorigin側の信頼済み同期スクリプト以外を実行せず、upstreamの内容はmergeするだけとする。

使用するGitHub Actionsとgh-aw actionはコンパイル時にcommit SHAへ固定する。`gh aw validate --strict` のschema、actionlint、shellcheck、zizmor、poutine検査を通す。

gh-aw v0.84.3が生成するClaude CLI導入stepはCLI自体をversion pinする一方、npm lockfileを使わないため、Zizmorの `adhoc-packages` だけを生成済み `upstream-sync.lock.yml` に限定して抑止する。他のZizmor監査は抑止しない。

## エラー処理

- upstream fetch失敗: 書き込みなしでrunを失敗させる。
- 差分なし: 成功として終了し、Claudeを起動しない。
- clean mergeのpush競合: 最新originから一度だけ再試行する。
- Claudeが解消不能: `origin/develop` を変更しない。既存PRがある場合は失敗概要を通知する。
- PR branchへのpush失敗: 後続のコメントやreviewer更新も停止し、部分更新を避ける。
- safe outputがprotected filesを検出: PRを保持し、人間のreview要求を付ける。
- 初回PR作成失敗: Issueは作成せず、Actions runを失敗させる。

## 検証

実装時に次を確認する。

1. 一時Git repositoryを使う同期処理テスト
   - upstreamとの差分なし
   - 競合なしのmerge commit作成
   - 競合検出と作業ツリーのcleanup
   - non-fast-forward再試行
2. `gh aw validate upstream-sync --strict`
3. `gh aw compile upstream-sync --validate --actionlint`
4. 生成された `.lock.yml` の権限、repository、branch、secret参照を目視確認
5. upstream向け書き込みコマンドやcross-repository safe outputが存在しないことを検索で確認

既存のアプリケーションテストにはbaselineで失敗とハングがあるため、この変更の合否には同期処理専用テストとgh-aw検証を用いる。既存テストの失敗は本変更では修正しない。

## 導入後の設定

PRのmerge後、次のrepository secretsを設定する。Workflowはsecret値をログ、PR、artifactへ出力しない。

- `ANTHROPIC_API_KEY`: 競合時のClaude実行に使用する。
- `UPSTREAM_SYNC_TOKEN`: `YumNumm/KyoshinEewViewerIngen` のみに限定したfine-grained PAT。Contents、Pull requests、Issuesのread/write権限を付与する。

初回は `workflow_dispatch` で起動し、Actions summary、直接同期または競合PR、reviewer、通知コメントを確認する。その後、毎日04:00 JSTの定期実行へ任せる。
