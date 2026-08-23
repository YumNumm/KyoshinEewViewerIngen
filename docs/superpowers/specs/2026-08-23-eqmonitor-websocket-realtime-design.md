# EQMonitor WebSocket リアルタイム更新 設計

## 目的

EQMonitor API の地震情報と緊急地震速報（EEW）のリアルタイム更新を、定期的な HTTP Fetch から WebSocket 配信へ移行する。HTTP は接続直後・再接続直後の一度限りの初期同期、履歴検索、ページング、個別詳細取得に限定する。

## 対象範囲

- EQMonitor 端末登録
- WebSocket チケット取得
- WebSocket の接続、heartbeat、再接続
- EEW upsert の反映（取消報を含む）
- 地震情報 upsert/delete の反映
- 接続直後と再接続直後の一度限りの HTTP 初期同期
- ポーリング設定と設定 UI の削除
- HTTP と WebSocket の User-Agent 送信

履歴検索、過去ページの追加読み込み、地震詳細取得は従来どおり HTTP を使用する。切断中の HTTP ポーリング・フォールバックは実装しない。

## API 契約

### 端末登録

端末 ID はクライアントで生成せず、接続先ごとに `POST /v2/device` で登録する。リクエストはプラットフォームにかかわらず次の値とする。

```json
{
  "type": "DESKTOP",
  "locale": "ja"
}
```

レスポンスの `deviceId` と登録に使用した正規化済みベース URL を設定へ永続化する。接続先 URL が変わった場合は新しい接続先で登録し直す。`deviceToken` はリアルタイム接続に不要かつ機密情報であるため、通常設定ファイルへ保存しない。

保存済み ID によるチケット取得が認証エラーまたは未登録エラーになった場合は、同じ接続試行内で一度だけ端末を再登録する。再登録が失敗した場合は通常の再接続バックオフへ移る。

### チケットとヘッダー

`GET /v2/realtime/ticket` へ `x-eqmonitor-device-id` を付け、返された `url` へ接続する。

EQMonitor の全 HTTP リクエストには次を送信する。

- `User-Agent`: `EqMonitorApiProvider.UserAgent`
- `x-eqmonitor-build`: `EqMonitorApiProvider.BuildNumber`
- `x-eqmonitor-device-id`: 登録後で、API が端末 ID を必要とする場合

WebSocket handshake にも同じ `User-Agent` と `x-eqmonitor-build` を送信する。チケットはメモリ内だけで扱い、設定やログへ保存しない。

### WebSocket メッセージ

外側のメッセージは `type` で判別する。

- `ready`: 接続準備完了
- `ping`: `{"type":"pong"}` を即時返信
- `pong`: クライアント起因 heartbeat の応答として受理
- `realtime`: `data.type` と `data.operation` で業務イベントを判別

対象の業務イベントは次のとおり。

- `eew` / `upsert`: `EewItemWithRelations` を反映
- `earthquake` / `upsert`: `Earthquake` を反映
- `earthquake` / `delete`: EQMonitor 由来の該当イベントを削除

端末登録、チケット、EEW、地震レコードには NSwag 生成型を使用する。`ready`、`ping`、`pong`、`realtime` の外側 envelope は、小さな WebSocket 専用パーサーで判別する。不明な型、未対応の業務イベント、不正 JSON は警告ログへ記録し、接続自体は継続する。

## コンポーネント

### `EqMonitorApiProvider`

既存の HTTP クライアント生成を維持し、最新 OpenAPI から生成した端末登録・チケット API を公開する。ベース URL の正規化、User-Agent、build 番号の責務を引き続き持つ。

### `EqMonitorDeviceService`

現在の接続先に対応する端末 ID を返す。未登録または接続先変更時は `DESKTOP` として登録し、返された ID と登録先を設定へ保存する。保存処理は既存の `ConfigurationLoader` を用いる。

### `EqMonitorRealtimeMessageParser`

受信した JSON を制御メッセージまたは生成型の業務イベントへ変換する。ネットワーク、設定、画面状態には依存しない純粋なパーサーとする。

### `EqMonitorRealtimeService`

アプリ内で一本だけの WebSocket 接続を所有する。端末登録、チケット取得、WebSocket handshake、受信ループ、heartbeat、初期同期調停、指数バックオフ、設定変更時のキャンセルを担当する。業務イベントは購読可能なイベントとして公開し、EEW と地震情報のサービスへ振り分ける。

再接続遅延は 1、2、4、8、16、32、60 秒とし、`ready` を受信した接続では試行回数をリセットする。EQMonitor 全体と EEW／地震情報のどちらかが有効な間だけ接続する。

### `EqMonitorEewSubscriber`

タイマーポーリングを削除し、`EqMonitorRealtimeService` の EEW イベントを購読する。報数による重複・逆行防止を維持し、取消報は `EewController.Cancelled`、通常報は `EewController.Update` へ渡す。受信時刻は `TimerService.CurrentTime` を使用する。

### `EqMonitorEarthquakeService`

定期 Fetch を削除し、地震 upsert/delete を購読する。取得済み署名とフラグメントのキャッシュ、受信元切替時の復元、初期同期、ページング、詳細取得は維持する。

地震 delete は EQMonitor 由来のキャッシュと、電文を持たない EQMonitor 由来イベントだけを削除する。同じイベント ID に気象庁電文由来のフラグメントが存在する場合は削除しない。

## 初期同期とイベント順序

`ready` 受信後、現在有効な種類だけ HTTP で一度同期する。

- EEW: `/v2/eew/latest`
- 地震情報: `/v2/earthquake` の先頭ページ

初期同期中に到着した業務イベントは接続単位のキューへ保持する。HTTP の結果を適用した後、保持イベントを受信順に適用して古い HTTP 応答による巻き戻りを防ぐ。一方の初期同期が失敗しても、失敗を日本語で一度記録し、その種類の保持イベントを解放して WebSocket 更新を継続する。

再接続時も同じ手順を行う。接続中に EEW または地震情報を新たに有効化した場合は、その種類だけ一度同期した後にイベント適用を開始する。

## 接続状態とエラー処理

- `ready` 受信後は WebSocket を接続済みとみなす。
- 切断時、EEW は `IsDisconnected = true`、地震情報は外部受信元を無効にする。
- 再接続はキャンセル可能な指数バックオフで行う。
- EQMonitor 全体無効、両種類無効、接続先変更、サービス破棄で接続と待機を即時キャンセルする。
- 初期同期失敗は WebSocket 切断理由にしない。
- 登録、チケット、接続、受信の同一失敗は復旧まで最初の一回だけ警告し、復旧時に情報ログを残す。
- WebSocket の接続・送受信・切断は既存の `NetworkDebugRecorder` へ記録する。URL のクエリに含まれるチケットは既存の秘匿処理を通す。

## 設定と UI

`EqMonitorConfig` から次を削除する。

- `EewPollingIntervalMs`
- `EarthquakePollingIntervalMs`

代わりに UI 非公開の登録情報を追加する。

- `DeviceId`
- `DeviceRegisteredBaseUrl`

設定画面から二つの「取得間隔」を削除し、EEW のポーリング遅延説明を削除する。「取得件数」は地震の初期同期と履歴ページングに使用するため維持する。

## OpenAPI 更新

`scripts/update-eqmonitor-openapi.py` の保持対象へ `/v2/device` と `/v2/realtime` を追加する。既存どおり、サーバ URL の除去、OpenAPI 3.1 nullable の正規化、enum 名の衝突回避を行い、生成ソースを手編集しない。

Realtime envelope の component が HTTP パスから直接到達不能な場合でも、WebSocket パーサーが必要とする `Earthquake` と `EewItemWithRelations` の生成型が保持されることをテストする。

## テスト

公開 API の振る舞いを中心に次を検証する。テストデータには実運用 URL を使用しない。

- OpenAPI 正規化後も既存のクエリ、enum、nullable、デシリアライズが維持される
- 端末未登録時に `POST /v2/device` が `DESKTOP` と `ja` で呼ばれ、返却 ID が保存される
- 接続先変更時と無効 ID 応答時に再登録される
- HTTP の User-Agent、build 番号、必要な device ID
- WebSocket handshake の User-Agent と build 番号
- `ready`、`ping`、`pong`、EEW upsert、地震 upsert/delete の解析
- 不明型と不正 JSON が致命的エラーにならない
- `ready` ごとの初期同期が一度だけで、定期 Fetch が存在しない
- 初期同期中に受信したイベントが HTTP 結果の後へ順序どおり適用される
- EEW の重複報、逆行報、取消報
- 地震 delete が電文由来イベントを削除しない
- 切断後のバックオフ再接続と設定無効化時のキャンセル

検証では関連 xUnit テスト、`KyoshinEewViewer` プロジェクト、`KyoshinEewViewer.Desktop` プロジェクトをビルドする。

## 対象外

- 切断中の HTTP ポーリング・フォールバック
- EQMonitor の通知設定や push token 管理
- `deviceToken` を用いた `/v2/device/me` 操作
- 津波、推計震度、揺れ検知の WebSocket 取り込み
- 既存の DM-D.S.S、Axis、強震モニタ接続の再構成
