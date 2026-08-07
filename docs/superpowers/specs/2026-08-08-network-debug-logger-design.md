# ネットワーク通信デバッグ機能 設計書

作成日: 2026-08-08

## 目的

アプリが行う HTTP / WebSocket 通信のリクエスト・レスポンス・ヘッダ・ボディを、
アプリ内のデバッグウィンドウから追跡できるようにする。

不具合調査時に「実際に何が飛んでいるのか」を外部ツール（プロキシ等）なしで確認することを狙う。

## 前提と方針

| 項目 | 決定 |
| --- | --- |
| 対象ビルド | Debug / Release 両方（`#if DEBUG` で囲まない） |
| 既定状態 | 無効。設定で明示的に有効化する |
| 設定の永続化 | する（`config.json`）。既定値は `false` |
| ボディの記録 | する。ただし種別・サイズでフィルタする |
| 対象プロトコル | HTTP 全般 + 素の `ClientWebSocket` 3 箇所 |
| エクスポート | 実装しない（端末内メモリのみ） |

`DmdataSharp` 内部の冗長 WebSocket 接続は、ライブラリが生フレームを公開していないため対象外とする。
公開されているのは `DataReceived` などのパース済みイベントのみで、他経路と記録の粒度が揃わないため、
無理に含めない。

## アーキテクチャ

### 追加するコンポーネント

```
src/KyoshinEewViewer/Services/NetworkDebug/
├── NetworkTransaction.cs      … 記録 1 件のモデル
├── NetworkDebugRecorder.cs    … リングバッファと通知（シングルトン）
├── NetworkCaptureHandler.cs   … HTTP 捕捉用の DelegatingHandler
├── NetworkCaptureMasker.cs    … 機微情報のマスキング
└── NetworkDebugHttpClient.cs  … HttpClient 生成ヘルパー
```

### データフロー

```
HttpClient ──▶ NetworkCaptureHandler ──┐
                                       ├──▶ NetworkDebugRecorder ──▶ MessageBus
ClientWebSocket 呼び出し元 ────────────┘                                  │
                                                                          ▼
                                                          DebugWindowViewModel（250ms バッファ）
                                                                          │
                                                                          ▼
                                                              DebugWindow「通信」タブ
```

`NetworkDebugRecorder` は既存の `Services/InMemoryLoggerProvider.cs` と同じ構造を採る。
`ConcurrentQueue` のリングバッファに保持し、追加を `MessageBus` で通知する。

## 各コンポーネントの仕様

### NetworkTransaction

記録 1 件を表すモデル。HTTP と WebSocket でフィールドが大きく異なるため、
共通の基底クラスと 2 つの派生クラスで表現する。

共通:

- `Id`（連番）
- `Timestamp`
- `Kind`（Http / WebSocket）

HTTP 固有:

- `Method`, `RequestUri`
- `RequestHeaders`, `RequestBody`
- `StatusCode`, `ResponseHeaders`, `ResponseBody`
- `ContentType`, `ContentLength`
- `Duration`
- `Error`（例外発生時のメッセージ）

WebSocket 固有:

- `Endpoint`
- `Direction`（Connect / Disconnect / Send / Receive）
- `MessageType`（Text / Binary / Close）
- `Payload`, `PayloadLength`

### NetworkDebugRecorder

- `SplatRegistrations.RegisterLazySingleton` で登録するシングルトン
- `IsEnabled` は `KyoshinEewViewerConfiguration.NetworkDebug.Enable` を参照する
- 保持件数の上限は **500 件**（定数）。超過分は古いものから捨てる
- `Record()` / `GetAll()` / `Clear()` を公開する
- 追加時に `MessageBus` へ `NetworkTransactionAdded` を送る

### NetworkCaptureHandler

`DelegatingHandler` を継承し、`SendAsync` で計測する。

1. `IsEnabled` が false なら即座に `base.SendAsync` へ委譲する。
   分岐 1 つのみなので、無効時のオーバーヘッドは実質ゼロになる
2. リクエストのヘッダとボディを収集する（マスキング適用）
3. `base.SendAsync` を実行し、所要時間を計測する
4. レスポンスのヘッダを収集する
5. ボディは `Content-Type` が `text/*`、`application/json`、`application/xml`、
   `application/x-www-form-urlencoded`、`application/javascript`、
   または `+json` / `+xml` で終わる形式の場合のみ取得する
6. 取得する場合は `LoadIntoBufferAsync()` でバッファ化してから読む。
   バッファ済みの `HttpContent` は呼び出し側が改めて読めるため、既存処理を壊さない
7. 記録する文字列は **256KB** で切り詰める。伏せ字を適用してから切り詰めることで、
   切断面に認証情報が残らないようにする
8. 例外発生時も記録する（`Error` にメッセージを入れ、例外は再スローする）

`Content-Length` による事前の足切りは行わない。アプリの大半の `HttpClient` は
`AutomaticDecompression` を有効にしており、その場合 `Content-Length` が取り除かれるため、
判定材料として使えないためである。上限つきの `LoadIntoBufferAsync(maxBufferSize)` も採用しない
— 上限超過で例外になった際にストリームが中途半端に消費され、呼び出し元の読み取りを壊すため。
代わりに Content-Type で対象を絞り、読み込み後に記録内容だけを切り詰める。

この Content-Type フィルタにより、強震モニタが 1〜2 秒毎に取得する PNG 画像は
自動的にボディ取得の対象外になる。一覧には行として現れるが、ボディは保持しない。

**キャプチャ処理の例外は通信本体へ伝播させない。**
記録に関わる処理はすべて try-catch で握りつぶし、失敗しても `base.SendAsync` の結果を
そのまま返す。防災アプリである以上、デバッグ機能が本体の通信を壊してはならない。

### NetworkCaptureMasker

ヘッダのマスキング対象（値を `***` に置換）:

- `Authorization`
- `Cookie`
- `Set-Cookie`
- `Proxy-Authorization`

JSON ボディのマスキング対象（キーに対応する値を `***` に置換）:

- `access_token`
- `refresh_token`
- `id_token`
- `client_secret`
- `code_verifier`

フォームエンコードされたボディでは、上記に加えて `code` も対象にする。
DM-D.S.S のトークン要求は `code` に認可コードを載せるためである。
一方で JSON の `code` は対象にしない。気象庁のデータが地域コード等で多用しており、
伏せると本来の用途を果たせなくなる。

WebSocket の接続先はクエリを丸ごと落とす（`NetworkCaptureMasker.SanitizeEndpoint`）。
DM-D.S.S の WebSocket URL はクエリに ticket を載せるためである。

HTTP の URL はマスクしない。EQMonitor API の `BaseUrl` は配布物に含めない非公開情報だが、
マスクすると機能の用途を果たせなくなる。記録が端末内メモリのみに留まり、
エクスポート機能を持たないことを前提とする。

### NetworkDebugHttpClient

既存の `HttpClient` 生成箇所を集約するための薄い static ヘルパー。

```csharp
public static HttpClient Create(HttpMessageHandler? innerHandler = null)
```

内側ハンドラを `NetworkCaptureHandler` で包んだ `HttpClient` を返す。
`KyoshinMonitorWatchService` の `SocketsHttpHandler` + `ConnectCallback` のような
特殊な構成も、内側ハンドラとして渡すだけで維持される。

## 既存コードへの変更

### HttpClient 生成箇所（本体 13 箇所）

| ファイル | 用途 |
| --- | --- |
| `Series/KyoshinMonitor/Services/KyoshinMonitorWatchService.cs` | 強震モニタ画像 |
| `Services/EqMonitor/EqMonitorApiProvider.cs` | EQMonitor API |
| `Services/TelegramPublishers/JmaXml/JmaXmlTelegramPublisher.cs` | 気象庁防災情報 XML |
| `Series/Radar/RadarSeries.cs` | 雨雲レーダー |
| `Services/TimerService.cs` | 時刻同期 |
| `Services/UpdateCheckService.cs` | 更新確認 |
| `Services/VoicevoxService.cs` | VOICEVOX |
| `Services/ExtarnalPublishers/Axis/AxisApiClient.cs` | Axis API |
| `Services/Workflows/BuiltinActions/WebhookAction.cs` | Webhook |
| `Series/KyoshinMonitor/Services/ObservationPointsUpdateService.cs` | 観測点更新 |
| `Services/TelegramPublishers/Dmdata/DmdataCustomSchemeAuthenticator.cs` | DM-D.S.S 認証 |
| `Series/Earthquake/EarthquakeSeries.cs` | 地震情報取得 |
| `Series/ObservationPointEditor/ViewModels/KyoshinImageMapViewModel.cs` | 観測点編集 |

`DmdataSharp` は `DmdataApiClientBuilder.UseOwnHttpClient()` があるため、
同じヘルパーで生成した `HttpClient` を注入できる。
`EqMonitorApiClient`（NSwag 生成）は既にコンストラクタ注入なのでそのまま渡せる。

### WebSocket（3 箇所）

- `Services/TelegramPublishers/Dmdata/DirectWebSocketController.cs`
- `Services/ExtarnalPublishers/Axis/AxisWebSocketConnection.cs`
- `Series/Lightning/LightningMapConnection.cs`

3 箇所とも構造が異なるため共通ラッパーは作らず、
接続・切断・送信・受信の各箇所で `NetworkDebugRecorder` を直接呼ぶ。

### 設定

`KyoshinEewViewer.Core/Models/KyoshinEewViewerConfiguration.cs` の既存の `DebugConfig` へ
`RecordNetworkTraffic`（既定 `false`）を追加する。デバッグ用の設定を集める場所が既にあるため、
専用のセクションは新設しない。

保持件数 500 件・ボディ上限 256KB は定数とし、設定項目には出さない。
調整が必要になってから公開すればよい。

## UI

`Views/DebugWindow.axaml` に「通信」タブを追加する（既存の 描画処理 / ログ / 設定 と並べる）。

- **ツールバー**: 記録の ON/OFF トグル、クリアボタン、ホスト絞り込み用 TextBox
- **一覧（DataGrid）**: 時刻 / 種別 / メソッドまたは方向 / ホスト / パス / ステータス / 所要時間 / サイズ
- **詳細ペイン**: 選択行のリクエストヘッダ・ボディ、レスポンスヘッダ・ボディ。
  `GridSplitter` で一覧と分割する

強震モニタにより毎秒行が増えるため、`MessageBus` の購読側で
`Buffer(TimeSpan.FromMilliseconds(250))` してまとめて追加する。
既存のログタブは 1 件ずつ追加しているが、通信タブは発生頻度が桁違いのためここは変える。

## テスト

`tests/KyoshinEewViewer.Tests/Services/NetworkCaptureHandlerTests.cs` を追加する。

`NetworkCaptureHandler` は記録先を省略可能なコンストラクタ引数として受け取る。
省略時は DI から解決するため通常の利用に影響はなく、テストでは記録先を直接渡せる。

検証項目:

- 無効時に記録されないこと
- Content-Type フィルタが効くこと（画像はボディを保持しない）
- サイズ上限を超えるボディを保持しないこと
- ヘッダとボディのマスキングが適用されること
- バッファ化した後も呼び出し側がレスポンスボディを読めること

テストデータには実在の URL を使わない（CLAUDE.md のテストデータ規約に従う）。

## 対象外とすること

- 記録内容のファイルエクスポート
- `DmdataSharp` 内部の冗長 WebSocket 接続の生フレーム取得
- 通信内容の編集・再送（リプレイ）
- 保持件数・ボディ上限の設定 UI 化
