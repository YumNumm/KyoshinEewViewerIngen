# iOS / iPadOS 版のメモ

`src/KyoshinEewViewer.iOS` (`net10.0-ios`) のプラットフォーム固有の制約と、その理由をまとめる。

## 通知

### フォアグラウンドにいる間だけ動作する

**iOS ではアプリがバックグラウンドに回ると速やかにサスペンドされ、強震モニタや電文の受信自体が
止まる。** プッシュ配信サーバーを持たない構成のため、通知が発生するのはアプリがフォアグラウンドに
ある間だけになる。画面を閉じている間に緊急地震速報を受け取ることはできない。

バックグラウンドでも通知するには、サーバー側から APNs でプッシュを送る仕組みが別途必要になる。

なお iOS はフォアグラウンドのアプリに対して既定でバナーを抑制する。そのため
`UNUserNotificationCenterDelegate.WillPresentNotification` で明示的に
`Banner | Sound | List` を返している。これが無いと通知が一切表示されない。

### 通常の通知と Critical Alert

`NotificationUrgency` を iOS の割り込みレベルへ対応させている。

| `NotificationUrgency` | iOS の扱い |
| --- | --- |
| `Low` | `Passive` (音を鳴らさず一覧に積むだけ) |
| `Normal` | `TimeSensitive` (時間指定モードを通す) |
| `Critical` | `Critical` + `DefaultCriticalSound` (おやすみモードと消音を貫通する) |

`Critical` は震度5弱以上・津波警報以上で使われる。**Apple の個別承認が必要な権限**で、
Bundle ID `net.yumnumm.KyoshinEewViewer` 側で `CRITICAL_ALERTS` を有効化済み。
`Entitlements.plist` の `com.apple.developer.usernotifications.critical-alerts` がこれに対応する。

権限が拒否された場合、Critical だけでなく通常の通知も送られない。

## 音声再生

`ManagedBass` は BASS のネイティブを動的ロードする前提だが、BASS の iOS 版は静的ライブラリしか
配布されておらず `DllImport("bass")` を解決できない (`DllNotFoundException` になる)。加えて BASS は
商用利用が有償のため、iOS では OS 標準の AVFoundation を使う。

共有側の `SoundPlayerService` / `VoicevoxService` は `IAudioBackend` 経由で音を鳴らすようにしてあり、
Splat に実装が登録されていなければ従来どおり BASS を使う。iOS ヘッドだけが
`IosAudioBackend` (`AVAudioPlayer`) を登録している。

### AVAudioSession

| 設定 | 理由 |
| --- | --- |
| カテゴリ `Playback` | 防災通知は消音スイッチやおやすみモードでも鳴らす必要がある。`Ambient` / `SoloAmbient` は消音スイッチで無音になる |
| オプション `DuckOthers` | 他アプリの再生を止めずに、鳴っている間だけ音量を下げる (`MixWithOthers` を暗黙に含む) |
| 再生中だけ `SetActive(true)` | ダッキングはセッションのアクティブ化と同時に始まるため、鳴っていない間もアクティブにしておくと他アプリの音量を下げ続けてしまう |
| 停止時に `NotifyOthersOnDeactivation` | これを付けないと他アプリが音量を戻すきっかけを得られない |

### BASS との差異

- `AVAudioPlayer` は OGG / FLAC を再生できない (WAV / MP3 / AAC / M4A / AIFF / CAF のみ)
- `AVAudioPlayer.Volume` は 0-1 のみで、BASS のような 1 超の増幅ができない
- 全体音量に相当する仕組みが無いため、チャンネル音量へ全体音量を掛け込んでいる

## ビルド

- **`UseInterpreter` が必須**。本体は System.Text.Json / MessagePack / Scriban とリフレクションを
  多用するため、AOT のみだと実行時に JIT を要求して `ExecutionEngineException` で落ちる
- **`AppIcon` プロパティが必須**。これが無いと actool にアプリアイコンとして渡されず
  `Assets.car` が生成されない。`Contents.json` は `scale` ではなく `size` で指定する
  (`size` が無いと「unassigned child」として無視される)
- `PublishReadyToRun` は使えない (AOT ツールチェーンが担当する)

## Info.plist

- **`UILaunchScreen` が必須**。無いとウィンドウのサイズが確定せず画面が真っ黒になる
- `UIRequiresFullScreen` を `false`、向きを全方向サポートにしないと iPad の
  マルチタスクとウィンドウリサイズが使えない
- `UIFileSharingEnabled` と `LSSupportsOpeningDocumentsInPlace` で、設定ファイルとログを
  「ファイル」アプリから取り出せるようにしている (保存先は Documents 配下)

## SafeArea

`MainView` はステータスバーを隠さず (`IsSystemBarVisible=true`)、その裏まで描画する
edge-to-edge を要求する。そのため地図とサイドバーの背景は画面全体に描画したまま、
シリーズ表示・サイドバーの項目・左下のボタン群だけを `SafeAreaPadding` の分だけ内側に寄せている。

`SafeAreaPadding` は物理ピクセルを `RenderScaling` で割った値だが、Android では
`RenderScaling` が 1 のまま `SafeAreaChanged` が先に飛んでくる。その値をそのまま使うと
物理ピクセル相当の過大な余白になるため、`TopLevel.ScalingChanged` でも取り直している。

## サブウィンドウ

iOS では `Window` を生成できないため、設定画面とセットアップウィザードは
`OverlaySubWindowsService` で `MainView` の上にオーバーレイとして表示する。

## URL スキームによる復帰 (DMDATA の認可)

ループバック HTTP サーバーを立てられないため、認可ページを外部ブラウザで開き、
カスタム URL スキームでアプリに戻ってきたところを拾う。

**購読は `AppDelegate` 自身の `IAvaloniaAppDelegate.Activated` で行う。**
`IActivatableLifetime` は `AvaloniaLocator` へ後から束縛されるため取得タイミングに
依存し、`Application.TryGetFeature` でも `null` になり得る。また iOS の
`SingleViewLifetime` は `IActivatableLifetime` を実装しないので、
`ApplicationLifetime` へのキャストは**常に失敗する**。

`UIApplicationSceneManifest` は不要。Avalonia の `application:openURL:options:` が
`ProtocolActivatedEventArgs` で `Activated` を発火する。

DMDATA 側には専用クライアントが必要で、リダイレクト URI とスコープの両方を
登録しておく必要がある。スコープが 1 つでも欠けると `invalid_scope` になる
(要求するのは `DmdataRedundantTelegramPublisher` の `RequiredScope` + `AdditionalScope`)。

## ファイルピッカーで選んだファイル

**ピッカーが返すパスは他アプリのコンテナ (`File Provider Storage`) を指しており、
選択直後しか開けない。** 設定として保存して後から使う場合は
`IStorageFile.OpenReadAsync()` で読み出して自身のコンテナへ複製すること。
生のパスを保存すると、再生時に `Exists:False` や `OSStatus -54` になる。

## 利用できない機能

| 機能 | 理由 |
| --- | --- |
| シリアル接続 (Qzss) | `System.IO.Ports` のネイティブが無い。USB シリアルも MFi なしでは扱えない |
| 自動更新 | App Store 配信では自己更新が禁止されている |

## 開発時の注意

- **上書きインストールすると `SIGKILL (Code Signature Invalid)` で起動しなくなる。**
  シミュレータへ入れる前に `xcrun simctl uninstall` すること
- `.app` を手で削除するとインクリメンタルビルドの整合性が崩れて署名が壊れる。
  クリーンにしたい場合は `obj` と `bin` ごと消す

## 配信

`scripts/publish-testflight-ios.sh` を参照。`.p12` やプロビジョニングプロファイルは用意せず、
無署名の `.app` から `.xcarchive` を組み立てて `xcodebuild -exportArchive` に
`-allowProvisioningUpdates` と ASC API キーを渡し、Apple 側に証明書とプロファイルを
自動発行させている (Cloud-managed certificates)。

`develop` への push で `.github/workflows/deploy-app.yaml` が iOS / macOS を
ビルドしてアップロードする。TestFlight のベータグループへの配布は
`testflight_group` を指定したときだけなので、push 由来のビルドは内部に留まる。

macOS (Mac App Store) はクラウド署名に対応していないため `.p12` が必要で、
`scripts/publish-testflight-macos.sh` を使う。

### CI の mise セットアップ

**`mise-action` に `install: false` を渡し、`mise install --yes` を別ステップで実行すること。**
アクションは `--yes` を渡さないため、プラグイン未導入の環境では同意待ちで失敗する。

`mise.lock` の dotnet は `core:dotnet` を使う。以前は `asdf:dotnet` だったが、
mise 2026.8 で dotnet が core プラグイン化して asdf バックエンドが使えなくなり、
fresh な環境でセットアップが必ず失敗していた。
