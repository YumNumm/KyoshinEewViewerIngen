# ReplayGenerator: 強震モニタ画像取得のリトライ

ReplayGenerator がリプレイファイル生成時に取得する強震モニタ画像は、公開遅延により一発で取得失敗するケースがある。これを軽減するため、`ReplayFileBuilder.FetchKyoshinImageAsync` に指数バックオフ付きのリトライを実装している。

## 挙動

- 1 秒ごとのフレームで強震モニタ画像 (`RealtimeImg`/`Shindo`) を取得する
- 失敗時（HTTP エラー or 例外）は指数バックオフで再試行する
- `MaxAttempts` を使い切っても取得できなかった場合は **そのフレームをスキップ**（既存挙動の踏襲）
- スキップ時は `WARNING` ログを出力する（理由: 公開遅延の場合は数秒で復旧することが多く、リプレイファイル生成全体を中断するより欠落フレームとして許容したほうが運用上有利）

## 設定

環境変数で調整できる。

| 環境変数 | デフォルト | 説明 |
|---|---|---|
| `KYOSHIN_IMAGE_RETRY_MAX_ATTEMPTS` | `3` | 最大試行回数（初回含む）。`1` でリトライ無し |
| `KYOSHIN_IMAGE_RETRY_INITIAL_DELAY_MS` | `500` | 初回リトライ前の待機時間 (ms) |
| `KYOSHIN_IMAGE_RETRY_BACKOFF_FACTOR` | `2.0` | 指数バックオフ倍率 |
| `KYOSHIN_IMAGE_RETRY_MAX_DELAY_MS` | `5000` | リトライ間の最大待機時間 (ms) |

例: デフォルト設定では `0ms → 500ms → 1000ms` の間隔で計 3 回試行する。

## 適用範囲

- 強震モニタ画像取得 (`FetchKyoshinImageAsync`) のみ対象
- EEW JSON 取得 (`FetchKyoshinEewJsonAsync`) は今回はリトライ対象外（Issue スコープ外）
