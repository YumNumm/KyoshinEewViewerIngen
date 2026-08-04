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

`MainView` は `IsSystemBarVisible=false` で全画面描画を要求するが、iPad はマルチタスクを
有効にしているとステータスバーを隠せない。そのため地図とサイドバーの背景は画面全体に描画したまま、
シリーズ表示・サイドバーの項目・左下のボタン群だけを `SafeAreaPadding` の分だけ内側に寄せている。

## サブウィンドウ

iOS では `Window` を生成できないため、設定画面とセットアップウィザードは
`OverlaySubWindowsService` で `MainView` の上にオーバーレイとして表示する。

## 利用できない機能

| 機能 | 理由 |
| --- | --- |
| 音声再生 | `ManagedBass` に iOS 用ネイティブが無く `DllNotFoundException` になる |
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
