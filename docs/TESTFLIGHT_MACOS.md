# macOS 版 TestFlight 配信手順

KyoshinEewViewer の macOS 版を Mac App Store / TestFlight 向けに署名済み pkg として
ビルドし、App Store Connect (以下 ASC) へアップロードするための手順書。

自動化スクリプトは `scripts/publish-testflight-macos.sh`。

> **注意**: リポジトリ直下の `build-macos.sh` と `docs/BUILD_MACOS.md` は陳腐化しており、
> 存在しない `net10.0-macos` TFM を参照しているため動作しない。参考にしないこと。

## 対象

| 項目 | 値 |
| --- | --- |
| ASC App ID | 未作成 (`APP_ID` で指定する) |
| Bundle ID | `net.yumnumm.KyoshinEewViewerIngen` |
| Team ID | `CPL7H8SHVM` |
| プラットフォーム | `MAC_OS` |
| アーキテクチャ | `osx-arm64` のみ (universal 化は「残課題」参照) |

> **アプリレコードの取り違え注意**: Bundle ID を `net.yumnumm.KyoshinEewViewerIngen` に
> 統一したため、ASC 上の既存レコードはいずれも使えない。新規に作成すること。
> ASC のアプリレコードは作成後に Bundle ID を変更できない。
>
> | App ID | Bundle ID | 名前 | |
> | --- | --- | --- | --- |
> | (新規作成) | `net.yumnumm.KyoshinEewViewerIngen` | KyoshinEewViewer for ingen | **こちらを使う** |
> | `6517347515` | `net.yumnumm.kevi` | KyoshinEewViewer for ingen | 旧。build 3 まで登録済みだが使わない |
> | `6502579684` | `net.yumnumm.KyoshinEewViewer.Desktop` | KyoshinMonitorViewer for Ingen | 使わない |
>
> プロビジョニングプロファイルは Bundle ID ごとに発行されるため、
> 別レコードへアップロードしようとしても署名が一致しない。
> `asc builds next-build-number` を bundle ID で引くと、指定を間違えたときに
> `sourcesConsidered: []` / `nextBuildNumber: 1` というもっともらしい値が返って
> しまい気付きにくいので、**App ID (数値) で指定する**のが安全。

### 実績

| build | build ID | ASC 上の状態 | 実際に起動するか |
| --- | --- | --- | --- |
| 2 | `31386ee9-c02c-46fa-90f2-ecfcdd130bc9` | VALID | **起動時に SIGABRT でクラッシュ** |
| 3 | `80ce3d2d-5d6c-4331-bc21-ac1c900b1fac` | VALID | launchservicesd の temporary exception を追加して修正 |

いずれも 2026-08-04 に version `1.0` としてアップロード
(`minOsVersion` 13.0、`usesNonExemptEncryption` false、90 日後に期限切れ)。

> **ASC の `VALID` はアプリが起動することを保証しない。** build 2 は VALID に
> なったが起動即クラッシュだった。必ず実機インストールで起動確認すること
> (詳細は「遭遇したエラー 6」)。

ad-hoc 配布 (GitHub Releases 経由) の bundle ID は
`net.ingen084.kyoshineewviewer` で別物。チェックイン済みの
`build-files/KyoshinEewViewer.Desktop.app/Contents/Info.plist` は ad-hoc 用の値のままにし、
TestFlight 用の値はスクリプトが**コピー後のバンドル側だけ**を PlistBuddy で上書きする。

## 前提: 署名資材の作成

作成済みの資材は `~/.kevi-signing/` にある (リポジトリ内へコピーしないこと)。
ゼロから作り直す場合の手順は以下。

### 1. Bundle ID

```bash
asc bundle-ids list --paginate
asc bundle-ids create --identifier "net.yumnumm.KyoshinEewViewerIngen" --name "KyoshinEewViewer for ingen" --platform MAC_OS
```

### 2. 証明書 (2 種類とも必要)

`.app` の署名用と `pkg` の署名用で別の証明書が必要。

```bash
# .app 署名用
asc certificates create \
  --certificate-type MAC_APP_DISTRIBUTION \
  --generate-csr \
  --key-out ~/.kevi-signing/mac_app_dist.key \
  --csr-out ~/.kevi-signing/mac_app_dist.csr

# pkg 署名用
asc certificates create \
  --certificate-type MAC_INSTALLER_DISTRIBUTION \
  --generate-csr \
  --key-out ~/.kevi-signing/mac_installer_dist.key \
  --csr-out ~/.kevi-signing/mac_installer_dist.csr
```

作成した証明書と秘密鍵を `.p12` にまとめてキーチェーンへ取り込むと
`codesign` / `productbuild` から identity として見えるようになる。

```bash
openssl x509 -inform DER -in ~/.kevi-signing/mac_app_dist.cer -out ~/.kevi-signing/mac_app_dist.pem
openssl pkcs12 -export \
  -inkey ~/.kevi-signing/mac_app_dist.key \
  -in ~/.kevi-signing/mac_app_dist.pem \
  -out ~/.kevi-signing/mac_app_dist.p12 \
  -passout "pass:$(cat ~/.kevi-signing/p12-password.txt)"
```

本番のログイン キーチェーンを汚さないよう、専用キーチェーンを作って検索リストに足している。

```bash
KP="$(cat ~/.kevi-signing/keychain-password.txt)"
security create-keychain -p "$KP" kevi-signing.keychain
security list-keychains -d user -s $(security list-keychains -d user | tr -d '"') kevi-signing.keychain
security unlock-keychain -p "$KP" kevi-signing.keychain
security import ~/.kevi-signing/mac_app_dist.p12 -k kevi-signing.keychain \
  -P "$(cat ~/.kevi-signing/p12-password.txt)" -T /usr/bin/codesign
# 非対話シェルから codesign を使うには partition list の設定が必須 (後述)
security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "$KP" kevi-signing.keychain
```

確認:

```bash
security find-identity -v | grep "3rd Party"
#   ... "3rd Party Mac Developer Application: Ryotaro Onoue (CPL7H8SHVM)"
#   ... "3rd Party Mac Developer Installer: Ryotaro Onoue (CPL7H8SHVM)"
```

### 3. プロビジョニングプロファイル

```bash
asc profiles create \
  --name "KEVI Mac App Store" \
  --profile-type MAC_APP_STORE \
  --bundle "net.yumnumm.KyoshinEewViewerIngen" \
  --certificate "<MAC_APP_DISTRIBUTION の証明書 ID>"

asc profiles download --id "<PROFILE_ID>" \
  --output ~/.kevi-signing/KEVI_MacAppStore.provisionprofile
```

内容の確認 (entitlements の突き合わせに使う):

```bash
asc profiles inspect --path ~/.kevi-signing/KEVI_MacAppStore.provisionprofile --entitlements --output table
```

## スクリプトの使い方

```bash
# ビルド番号を ASC から自動取得してアップロードまで実行
./scripts/publish-testflight-macos.sh

# pkg 作成まで (アップロードしない)
SKIP_UPLOAD=true ./scripts/publish-testflight-macos.sh

# ビルド番号を明示
BUILD_NUMBER=3 ./scripts/publish-testflight-macos.sh
```

主な環境変数 (すべて既定値あり):

| 変数 | 既定値 | 用途 |
| --- | --- | --- |
| `APP_ID` | なし (指定必須) | ASC のアプリ ID |
| `BUNDLE_ID` | `net.yumnumm.KyoshinEewViewerIngen` | CFBundleIdentifier |
| `TEAM_ID` | `CPL7H8SHVM` | entitlements の team-identifier |
| `APP_VERSION` | `1.0` | CFBundleShortVersionString |
| `BUILD_NUMBER` | ASC から自動取得 | CFBundleVersion |
| `RID` | `osx-arm64` | .NET RuntimeIdentifier |
| `SIGNING_IDENTITY_APP` | `3rd Party Mac Developer Application: ...` | `.app` 署名 identity |
| `SIGNING_IDENTITY_INSTALLER` | `3rd Party Mac Developer Installer: ...` | pkg 署名 identity |
| `PROVISIONING_PROFILE` | `~/.kevi-signing/KEVI_MacAppStore.provisionprofile` | 埋め込むプロファイル |
| `SIGNING_KEYCHAIN` | (空) | 明示したい場合のキーチェーン |
| `OUTPUT_DIR` | `out/testflight-macos` | 成果物の出力先 (git-ignore 済み) |
| `SKIP_UPLOAD` | `false` | `true` で pkg 作成まで |
| `ASC_BIN` | mise の asc | asc のパス |

ビルド番号は手で決めずに ASC へ問い合わせるのが安全。

```bash
asc builds next-build-number --app "$APP_ID" --version 1.0 --platform MAC_OS
# {"latestProcessedBuildNumber":"1", ... ,"nextBuildNumber":"2"}
```

2024-07-05 に version `1.0` / build `1` をアップロードした実績があり (現在は期限切れ)、
期限切れ・削除済みのビルド番号も再利用できないため、次に使えるのは `2` 以降。

## スクリプトが行っていること

1. **`dotnet publish`** — `net10.0` / `osx-arm64` / `--self-contained` / `PublishSingleFile=false`
2. **`.app` レイアウト** — `build-files/KyoshinEewViewer.Desktop.app` をコピーし、
   publish 出力を丸ごと `Contents/MacOS` へ配置 (既存の `publish-kevi` action と同じ構成)
3. **`Info.plist` 修正** — bundle ID / バージョン / カテゴリなどを PlistBuddy で上書き
4. **プロファイル埋め込み** — `Contents/embedded.provisionprofile`
5. **entitlements 生成** — App Sandbox 必須 (後述)
6. **署名** — ネイティブバイナリ (内側) → `.app` 本体 (外側) の順
7. **`productbuild`** で pkg 化 → installer 証明書で署名
8. **`asc builds upload`** → `asc builds wait` で VALID になるまで待機

### Info.plist に追記している値

| キー | 値 | 理由 |
| --- | --- | --- |
| `CFBundleIdentifier` | `net.yumnumm.KyoshinEewViewerIngen` | テンプレートは ad-hoc 用の値なので上書き必須 |
| `CFBundleShortVersionString` | `APP_VERSION` | テンプレートは `KEVI_VERSION` プレースホルダ |
| `CFBundleVersion` | `BUILD_NUMBER` | ASC 側のビルド番号と一致させる |
| `LSApplicationCategoryType` | `public.app-category.weather` | App Store ではカテゴリ必須 |
| `LSMinimumSystemVersion` | `13.0` | 下記参照。テンプレートの `10.12` は非現実的 |

`LSMinimumSystemVersion` の `13.0` の根拠: .NET 10 がターゲットにできる最小の macOS は
12 だが、Microsoft が実際にテストしているのは Apple がサポート中のバージョン
(執筆時点で macOS 14 以降) のみ。2024 年にアップロードされた build 1 も
`minOsVersion` が `13.0` だったため、それに揃えている。
実際にどこまで下げられるかは動作検証が必要 (「残課題」参照)。
| `ITSAppUsesNonExemptEncryption` | `false` | 輸出コンプライアンスの質問をスキップ |

### entitlements

App Store 配布では App Sandbox が必須。

| entitlement | 理由 |
| --- | --- |
| `com.apple.security.app-sandbox` | App Store 配布の必須要件 |
| `com.apple.security.cs.allow-jit` | .NET ランタイムの JIT に必要 |
| `com.apple.security.network.client` | 地震データ (強震モニタ / Dmdata / JMA) の取得 |
| `com.apple.security.files.user-selected.read-write` | ファイルピッカー経由の読み書き |
| `com.apple.application-identifier` | `CPL7H8SHVM.net.yumnumm.KyoshinEewViewerIngen`。プロファイルと一致必須 |
| `com.apple.developer.team-identifier` | `CPL7H8SHVM`。プロファイルと一致必須 |
| `com.apple.security.temporary-exception.mach-lookup.global-name` | `com.apple.coreservices.launchservicesd`。**これが無いと起動時に必ずクラッシュする**。詳細は「遭遇したエラー 6」 |

## 遭遇したエラーと解決策

GitHub Actions 化の際にも同じ罠を踏むため、時系列で記録する。

### 1. Release ビルドが `CS0103: 'DebugPen' という名前は存在しません` で失敗

```
src/KyoshinEewViewer.Map/Layers/ImageTileLayer.cs(116,75): error CS0103: ...
```

`DebugPen` は `#if DEBUG` 内で宣言されているが、コミット `4886c82a`
("chore: disable debug branch") が 116〜117 行目を囲っていた `#if DEBUG` を
削除してしまい、`Release` 構成でのみコンパイルが通らなくなっていた。
Debug ビルドでは再現しないため気付きにくい。

**解決**: 116〜117 行 (タイル境界のデバッグ描画) を `#if DEBUG` で囲み直した。
Release ではプレースホルダのハッチ (`PlaceHolderPaint`) のみ描画される。

**教訓**: TestFlight 用は必ず `-c Release` なので、CI に Release ビルドが無いと
この種の退行を検出できない。

### 2. `asc` の API 系コマンドが全てタイムアウトでハングする

`asc builds list` / `builds next-build-number` / `auth issuer-id` / `auth doctor`
などが数分待っても応答せず、`--debug --api-debug` を付けても stderr に
1 行も出力されない。一方で以下は正常だった。

- `asc --version` / `asc --help` / `asc capabilities`
- `asc auth status` (プロファイルのメタデータのみ読む)
- `asc profiles inspect --path ...` (ローカルファイルのみ)

つまり**キーチェーンから API 秘密鍵を読み出す段階でブロック**していた。
ログ初期化より前に止まるため、デバッグ出力が一切出ない点が切り分けの鍵。

**原因**: asc は認証情報を macOS キーチェーンへ保存する。キーチェーン項目の
ACL に登録されていないプロセスが読むと GUI の許可ダイアログが出るため、
非対話シェルからは応答待ちで停止して見える。
(asc のバイナリがセットアップ後に mise で入れ替わり、ACL が一致しなくなっていた。)

**解決**: **一度だけ許可すれば以後はキャッシュされる**。バックグラウンドで
実行したまま許可ダイアログを承認したところ、以降は同じコマンドが 4〜5 秒で
完了するようになった。

```bash
# 承認後
asc builds list --app "$APP_ID" --limit 5   # 4.5s で完了
```

> **この不具合は初回のみ発生する時間依存の症状**で、承認後に検証した人には
> 絶対に再現しない。実際にこの調査中、別の作業者が承認後に同じコマンドを実行して
> 「ハングは再現しない、原因はキーチェーンではない」と結論づけたが、これは
> 仮説の反証ではなく実行順序による偽陰性だった。
> キャッシュされる副作用 (キーチェーン ACL、認証トークン、DNS 等) が絡む不具合は、
> 後から実行した観測者には再現しない点に注意。

**回避策 (CI / 非対話環境)**: キーチェーンを使わず環境変数で認証する。

```bash
export ASC_BYPASS_KEYCHAIN=1
export ASC_KEY_ID=...
export ASC_ISSUER_ID=...
export ASC_PRIVATE_KEY_PATH=/path/to/AuthKey_XXXX.p8   # または ASC_PRIVATE_KEY_B64
```

GitHub Actions では必ずこの方式にすること。キーチェーン方式は対話的な承認が
必要になり得るため CI では使えない。

### 3. `PublishSingleFile=true` は App Store 配布に使えない

既存の `.github/actions/publish-kevi/action.yml` は `PublishSingleFile=true` を
使っているが、単一ファイル形式は起動時にネイティブライブラリを一時ディレクトリへ
展開するため、App Sandbox と署名検証の両方と相性が悪い。

**解決**: TestFlight 用は `PublishSingleFile=false` (フォルダ形式) で publish し、
`Contents/MacOS` 内のネイティブバイナリを個別に署名する。

### 4. `code object is not signed at all` — `Contents/MacOS` の全ファイルに署名が必要

`.app` 本体の署名が以下で失敗する。

```
KyoshinEewViewer.app: code object is not signed at all
In subcomponent: .../Contents/MacOS/FluentAvalonia.dll
```

`.dylib` だけを署名しても解決せず、潰すたびに別のファイル名で再発する
(`FluentAvalonia.dll` → `KyoshinEewViewer.Desktop.runtimeconfig.json` → ...)。

**原因**: `codesign` は `Contents/MacOS` を **nested code の置き場所**として扱う。
そのため**メイン実行ファイル以外の全ファイル**が独立した署名を要求される。
ネイティブバイナリやマネージドアセンブリだけでなく、
`.deps.json` / `.runtimeconfig.json` のような単なるデータファイルも対象。
self-contained publish は 180 個以上のファイルをここへ吐くため、
種別で絞り込む方針だと必ず取りこぼす。

**解決**: 種別を判定せず、メイン実行ファイル以外を無条件に全て署名する。
非 Mach-O ファイルの署名は拡張属性 `com.apple.cs.CodeSignature` に格納される
(`productbuild` の pax ペイロードは拡張属性を保持するので pkg 化しても残る)。

```bash
find "$APP/Contents/MacOS" -type f -print0 |
  while IFS= read -r -d '' f; do
    [ "$f" = "$MAIN_EXECUTABLE" ] && continue
    codesign --force --timestamp --options runtime --sign "$IDENTITY" "$f"
  done
# 最後にバンドル本体へ entitlements 付きで署名
codesign --force --timestamp --options runtime \
  --entitlements "$ENTITLEMENTS" --sign "$IDENTITY" "$APP"
```

**メイン実行ファイルを個別に署名しないこと**: `Contents/MacOS/KyoshinEewViewer.Desktop`
を単体で `codesign` へ渡すと、codesign がバンドル署名へ読み替えてしまい、
まだ未署名の nested code を理由に途中で失敗して原因を見誤る。

**`--deep` を使わないこと**: 全ファイルへ同じ entitlements が付いてしまう。
内側 (個別ファイル) → 外側 (`.app` に entitlements 付き) の順で署名する。
既存の ad-hoc 配布 (`.github/actions/publish-kevi/action.yml`) が
`codesign --force --deep --sign -` で通っているのは、ad-hoc 署名かつ
entitlements 不要だからで、App Store 配布では踏襲できない。

### 5. アップロードは成功するが ASC 側の処理が `FAILED` になる (90255 / 90296 / 90240)

```
Upload committed in App Store Connect.
Error: builds upload: build upload "81e8b288-..." failed with state FAILED: 90255, 90296, 90240
```

`asc` はエラーコードしか返さない。詳細は以下で確認できるが、やはりコードのみ。

```bash
asc builds uploads list --app "$APP_ID" --output json
asc builds uploads view --id "<UPLOAD_ID>" --output json --pretty
```

Apple からの通知メールには文章が載るが、CLI からは読めないためコードから逆引きする。
3 つとも原因が別だったので、それぞれ以下に記録する。

#### ITMS-90255 — root しか読めないファイルがある

> The installer package includes files that are only readable by the root user.
> This will prevent verification of the application's code signature when your app is run.

**原因**: `Contents/embedded.provisionprofile` のパーミッションが `600` だった。
`~/.kevi-signing/` 配下の資材は `600` で保存してあり、`cp` はコピー元のモードを
引き継ぐため、そのままバンドルへ入っていた。

**解決**: 署名前にバンドル全体のパーミッションを正規化する
(ディレクトリ `755` / 通常ファイル `644` / Mach-O のみ `755`)。
`chmod` は署名を無効化するため**必ず署名より前**に実施する。

```bash
find "$APP" -type f ! -perm -o+r   # 事前確認: 何も出なければ OK
```

#### ITMS-90296 — App Sandbox が有効になっていない実行ファイルがある

> App Sandbox not enabled. The following executables must include the
> `com.apple.security.app-sandbox` entitlement with a Boolean value of true...

**原因**: `.app` 本体には entitlements を付けていたが、`Contents/MacOS/createdump`
という**もう 1 つの Mach-O 実行ファイル**が entitlements なしで同梱されていた。
`createdump` は .NET のクラッシュダンプ採取用ヘルパーで、self-contained publish が
自動的に出力する。

確認方法 (メイン実行ファイル以外の実行ファイルを洗い出す):

```bash
find "$APP/Contents" -type f -print0 | while IFS= read -r -d '' f; do
  case "$(file -b "$f")" in *"Mach-O"*"executable"*) echo "$f";; esac
done
```

**解決**: `createdump` を削除した。`DOTNET_DbgEnableMiniDump` を設定した場合しか
使われないためアプリの動作には影響しない。
(残す場合は `com.apple.security.app-sandbox` +
`com.apple.security.inherit` を付けて個別に署名する必要がある。)

#### ITMS-90240 — Unsupported Architectures (i386 スライス)

> Your executable contained the following disallowed architectures: i386

**原因**: リポジトリ同梱の `src/KyoshinEewViewer.Desktop/libs/osx/libbass.dylib` が
`x86_64 / i386 / arm64` の fat binary で、32bit の i386 スライスを含んでいた。
`osx-arm64` 向けにビルドしていても、同梱される dylib のスライスはそのまま残る。

確認方法:

```bash
find "$APP/Contents" -type f -print0 | while IFS= read -r -d '' f; do
  case "$(file -b "$f")" in *Mach-O*) printf '%s\t%s\n' "$(lipo -archs "$f")" "$f";; esac
done
# x86_64 i386 arm64  .../Contents/MacOS/libbass.dylib   ← これ
```

**解決**: 署名前に `lipo -remove i386` で i386 スライスを除去する。
`lipo` は署名を壊すため**必ず署名より前**に実施する。

> なお失敗したアップロードはビルド番号を消費しない。
> `asc builds next-build-number` の `sourcesConsidered` は `processed_builds` のみで、
> `FAILED` のアップロードは無視されるため同じ番号で再挑戦できる。

#### この 3 件と起動クラッシュは別問題（混同しやすい）

この 3 件を修正した結果 build 2 は `VALID` になったが、それでも
**起動時クラッシュは残った**（原因は次の「6」の launchservicesd）。

Apple から届く「配信の問題」メールは **`FAILED` になったアップロード**について
送られてくる。build 2 は 1 回目のアップロードが `FAILED`（この 3 件）、
2 回目が `VALID` なので、メールの内容は 1 回目のものであり、
**メールが届いていることは修正が入っていないことを意味しない**。
`asc builds uploads list --app <ID>` で `FAILED` / `COMPLETE` を突き合わせて
どのアップロードに対する指摘かを確認すること。

#### 再発防止のための事前検証

この 3 件はいずれもアップロード後（約 15 分後）に判明するため、
スクリプトは署名前とアップロード前に自前で検証して早期に落とすようにしている。

```bash
# i386 スライスの残存 (ITMS-90240)
lipo -archs <各 Mach-O>
# メイン実行ファイル以外の Mach-O 実行体 (ITMS-90296)
file -b <各ファイル> | grep "Mach-O.*executable"
# root しか読めないファイル (ITMS-90255) — .app と、展開した pkg の両方を見る
find <App>.app ! -perm -o+r
pkgutil --expand-full <out>.pkg <tmp> && find <tmp> ! -perm -o+r
```

pkg 側も見るのは、`.app` を正規化しても `productbuild` の入力資材が
差し替わると再発し得るため。

### 6. VALID になったビルドが起動時に SIGABRT でクラッシュする（最重要）

build 2 は ASC 上で `VALID` になったが、TestFlight からインストールすると
起動時に必ず落ちた。**ASC の処理完了はアプリが動くことを何も保証しない。**

```
Abort trap: 6 / abort() called
_RegisterApplication(), unable to get application ASN from launchservicesd
kernel: Sandbox: KyoshinEewViewer.Desktop(45969) deny(1) mach-lookup
        com.apple.coreservices.launchservicesd
```

クラッシュレポート (`~/Library/Logs/DiagnosticReports/*.ips`) のバックトレース:

```
libAvaloniaNative.dylib   (NSApplication へ最初のアクセス)
AppKit                    +[NSApplication sharedApplication]
AppKit                    -[NSApplication init]
AppKit                    _NSInitializeAppContext
AppKit                    +[NSMenuBarPresentationInstance _isMenuBarVisible]
AppKit                    _NSGetAggregateUIMode
HIServices                GetCurrentProcess          ← 旧 Carbon API
HIServices                _RegisterApplication
                          abort()
```

**根本原因**: Avalonia は初期化中に `[NSApplication sharedApplication]` を呼ぶ。
AppKit はその中でメニューバーの状態を調べるために旧 Carbon API の
`GetCurrentProcess()` を経由し、`_RegisterApplication()` が ASN 取得のため
`com.apple.coreservices.launchservicesd` への mach-lookup を行う。
ところが Apple の App Sandbox プロファイルは
**LaunchServices の mach サービスを一切許可していない**。

```bash
# application.sb / system.sb / appsandbox-common.sb のいずれにも記述が無いことを確認できる
grep -n "launchservicesd" /System/Library/Sandbox/Profiles/application.sb   # ヒット無し
```

非サンドボックスの ad-hoc 配布ではこの lookup が通ってしまうため、
**サンドボックスを有効にした App Store 版でしか発現しない**。
Avalonia 側の既知の問題でもある (AvaloniaUI/Avalonia issue #6529、未解決)。

**解決**: entitlements に launchservicesd の temporary exception を追加する。

```xml
<key>com.apple.security.temporary-exception.mach-lookup.global-name</key>
<array>
	<string>com.apple.coreservices.launchservicesd</string>
</array>
```

#### 切り分けの実験結果

Apple Development 証明書で再署名し、テスト用 bundle ID
(`net.yumnumm.kevi.sbtest`) で二分探索した結果:

| 構成 | 結果 |
| --- | --- |
| sandbox + 全 entitlements + hardened runtime | クラッシュ |
| 上記 + `CFBundleSupportedPlatforms` 追加 | クラッシュ (Info.plist は無関係) |
| sandbox + 全 entitlements、hardened runtime なし | クラッシュ (hardened runtime は無関係) |
| sandbox + **launchservicesd の temporary exception** | **起動・継続動作** |

つまり entitlements の数を増やしたことや Info.plist の不足キーは原因ではなく、
**launchservicesd への lookup 一点**が原因。

#### 起動テストの注意（重要）

- **エージェントのシェルから `open` や実行ファイル直起動でテストしても無意味**。
  サンドボックス内から起動した GUI アプリは同じ `_RegisterApplication` abort に
  なるため、原因の切り分けができない (openai/codex issue #30043)。
  必ず Finder 経由の正規 GUI 起動を使う。

  ```bash
  osascript -e 'tell application "Finder" to open POSIX file "/path/to/App.app"'
  ```

- 成否は `pgrep -f "<App>.app/Contents/MacOS"` と
  `~/Library/Logs/DiagnosticReports` の新規 `.ips` 件数で判定する。
- 対照実験に既知のサンドボックス化 MAS アプリ (例 `/Applications/CrystalFetch.app`)
  を同じ方法で起動し、環境側の問題でないことを確認しておくとよい。
- 反復を速くするため、ネストされた 181 ファイルの署名は**一度だけ**行い、
  以降は `.app` 本体だけ再署名する (entitlements を変えても
  ネスト署名は有効なまま)。全署名し直すと 1 回 3 分かかる。

#### temporary exception のリスク

`temporary-exception` は Apple が用意した正規の仕組みだが、Mac App Store の
**審査で理由を問われる可能性がある**。将来的には Avalonia 側で
`[NSApplication sharedApplication]` の呼び出し経路を変えて lookup 自体を
不要にするのが本来の解決 (「残課題」参照)。

## Universal (arm64 + x86_64) 化は現状できない

結論: **self-contained な .NET アプリでは、1 つの `.app` を universal 化できない。**
`lipo` で結合できるのはネイティブバイナリだけで、マネージドアセンブリが
アーキテクチャ固有だから。以下は実測に基づく。

### 実測 1: publish 出力の 183 ファイルのうち 153 が RID 間で異なる

```bash
dotnet publish ... -r osx-arm64 -o out/osx-arm64
dotnet publish ... -r osx-x64   -o out/osx-x64
# 各ファイルを shasum -a 256 で比較
# → 同一 30 件 / 差分 153 件 (うち .dll が 135 件)
```

原因は `common.props` の **`PublishReadyToRun=true`**。R2R は各アセンブリに
対象アーキテクチャのネイティブコードを埋め込むため、マネージド `.dll` の
大半が RID 固有になる。PE ヘッダの machine 値でも確認できる。

| | arm64 publish | x64 publish | 件数 |
| --- | --- | --- | --- |
| R2R 済みアセンブリ | `0xec20` | `0xc020` | 135 |
| IL のみ (アーキ非依存) | `0x014c` (I386) | `0x014c` (I386) | 25 |

`.dll` は Mach-O ではないので `lipo` は使えない。

### 実測 2: 「arm64 の .dll を x64 でも使えばよい」は成立しない

x64 の publish 出力に arm64 publish のマネージド `.dll` を被せて
Rosetta で起動すると、マネージドコードに到達する前に落ちる。

```
Failed to load System.Private.CoreLib.dll (error code 0x8007000B)
Error message: An attempt was made to load a program with an incorrect format.
Failed to create CoreCLR, HRESULT: 0x8007000B
```

R2R イメージは IL も持つため通常のアセンブリはアーキ不一致なら JIT へ
フォールバックする。しかし **`System.Private.CoreLib.dll` は例外**で、
CoreCLR は machine 不一致の CoreLib を拒否し、CoreCLR の生成自体に失敗する。
フォールバックは効かない。

なお `PublishReadyToRun=false` にしても解決しない。`System.Private.CoreLib.dll`
はランタイムパックから来る時点で既にアーキごとに crossgen 済みのため。

### 実測 3: lipo 結合は「成功」するが x64 スライスは起動しない

実際に lipo 結合方式を最後まで実装して両スライスを動かした結果。

```
lipo 結合: 21 件 / スキップ: 0 件
全 Mach-O のアーキテクチャ分布: 21 件すべて "x86_64 arm64"
```

| スライス | 結果 |
| --- | --- |
| arm64 (ネイティブ) | 起動し動作継続（バージョン出力まで到達） |
| x86_64 (Rosetta 2) | `Failed to create CoreCLR, HRESULT: 0x8007000B` |

**注意すべき点**: ネイティブバイナリの結合自体は完全に成功し、
「全 Mach-O が `x86_64 arm64` になっていること」という検証は**通ってしまう**。
i386 も自動的に落ちる（arm64 スライスは arm64 publish から、x86_64 スライスは
x64 publish から取り出して `lipo -create` すればよい）。
それでも x64 側は CoreCLR の初期化前に落ちる。
**`lipo -archs` の検証は必要条件でしかなく、universal 化の成功を意味しない。**
アップロードしてしまうと Intel Mac 利用者にだけ起動不能なアプリが届く。

### 参考

`dotnet/sdk` issue #33469「Cannot create universal macos binaries (x64+arm)」は
クローズされているが未解決。アーキごとにマネージドアセンブリのフォルダが
異なる点が同じ理由として挙げられている。

検討して却下した他の案:

- **`PublishReadyToRun=false`** — R2R 由来の差分 135 件は消えるが、
  `System.Private.CoreLib.dll` はランタイムパック時点で crossgen 済みなので残る。
- **deps.json の `runtimes/<rid>/` サブフォルダ方式** — framework-dependent 配置の
  仕組みで、self-contained では publish 時に RID が確定するため使えない。
  `libcoreclr.dylib` を fat にしても CoreLib は隣に 1 つしか置けない。
- **NativeAOT (`PublishAot`)** — 単一ネイティブバイナリになるので lipo 可能だが、
  本アプリは Scriban / ReactiveUI / リフレクションを多用し、
  現状でも大量の IL2026 トリム警告が出ているため現実的でない。

### 取り得る選択肢

| 方針 | Apple Silicon | Intel Mac | 備考 |
| --- | --- | --- | --- |
| `osx-arm64` のみ（現状） | ネイティブ | **非対応** | Intel には配信されない |
| `osx-x64` のみ | Rosetta 2 経由 | ネイティブ | 1 バイナリで両対応。ただし主要環境が Rosetta になる |
| ランチャ shim で 2 ランタイム同梱 | ネイティブ | ネイティブ | 下記の理由で非推奨 |

`osx-x64` 単一は実測で Apple Silicon + Rosetta 2 での起動を確認済み
（`arch -x86_64` でバージョン出力まで到達）。ただしリアルタイム監視アプリの
主要動作環境が Rosetta になる代償は大きい。

ランチャ shim 方式（`Contents/MacOS` に universal の小さな起動専用バイナリを置き、
アーキ別サブディレクトリの本体を `exec` する）は理屈の上では可能だが、
- 本体が「メイン実行ファイル以外の Mach-O 実行体」になり ITMS-90296 の対象
  （`app-sandbox` + `com.apple.security.inherit` を付けて個別署名が必要）
- `exec` 後のプロセスが LaunchServices からアプリとして認識されるか不明で、
  すでに苦労している `_RegisterApplication` 問題（「遭遇したエラー 6」）を
  悪化させる可能性が高い

ため、App Store 配布では非推奨。**どの方針を採るかはプロダクト判断が必要。**

## GitHub Actions 化に向けて

### 必要なシークレット

| シークレット | 中身 | 取得元 |
| --- | --- | --- |
| `MAC_APP_DIST_P12_BASE64` | `.app` 署名証明書 | `base64 -i ~/.kevi-signing/mac_app_dist.p12` |
| `MAC_INSTALLER_DIST_P12_BASE64` | pkg 署名証明書 | `base64 -i ~/.kevi-signing/mac_installer_dist.p12` |
| `MAC_P12_PASSWORD` | 上記 2 つの `.p12` のパスワード | `~/.kevi-signing/p12-password.txt` |
| `MAC_PROVISIONING_PROFILE_BASE64` | プロビジョニングプロファイル | `base64 -i ~/.kevi-signing/KEVI_MacAppStore.provisionprofile` |
| `ASC_KEY_ID` | ASC API キー ID (`JD4HMGS6HZ`) | `asc auth status` |
| `ASC_ISSUER_ID` | ASC の issuer ID (UUID) | **`asc auth issuer-id`** |
| `ASC_PRIVATE_KEY_B64` | `AuthKey_XXXX.p8` を base64 化 | `base64 -i ~/.asc/AuthKey_JD4HMGS6HZ.p8` |

issuer ID の実値はこのリポジトリが public なため意図的に記載しない。
`asc auth issuer-id` でいつでも取得できる (「Print the active App Store Connect
issuer ID」)。秘密鍵 `.p8` は ASC から再ダウンロードできないため、
`~/.asc/AuthKey_JD4HMGS6HZ.p8` を失わないこと。

`.gitignore` には既に `*.p8` / `*.p12` / `*.cer` / `*.csr` /
`*.provisionprofile` / `AuthKey_*.p8` / `signing/` の除外ルールがある
(「署名関連の秘密情報 (誤コミット防止)」ブロック) ので、
検証で資材をリポジトリ内へ置いても誤コミットは防げる。とはいえ
`~/.kevi-signing/` から**リポジトリ内へコピーしないのが原則**。

### ワークフロー側で必要な処理

1. **一時キーチェーンの作成と破棄** — ランナーのキーチェーンへ `.p12` を取り込み、
   `security set-key-partition-list` を実行する。これを忘れると `codesign` が
   ハングまたは `errSecInternalComponent` で失敗する。
2. **`ASC_BYPASS_KEYCHAIN=1` + 環境変数認証** — 「遭遇したエラー 2」参照。
   キーチェーン方式は対話承認が必要になり得るため CI では使えない。
3. **`mise install` には `GITHUB_TOKEN` が必要** — `asc` は
   `github:rorkai/App-Store-Connect-CLI` から取得しているため、
   トークン無しでは GitHub API のレート制限に当たる。
   `env: GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}` を渡すこと。
4. **`macos-latest` (Apple Silicon) を使う** — `osx-arm64` をビルドするため。
5. **ビルド番号** — `asc builds next-build-number` の結果を使う。
   `github.run_number` は ASC 側の履歴と無関係なので衝突し得る。
6. **CI 向けに設定しておくとよい `asc` の環境変数**
   - `ASC_SPINNER_DISABLED=1` — スピナーを抑止する。CI ログが制御文字で汚れない
   - `ASC_MAX_RETRIES` / `ASC_BASE_DELAY` / `ASC_MAX_DELAY` — ASC API は不安定な
     ことがあるためリトライを調整できる。`ASC_RETRY_LOG=1` で挙動を確認できる
   - `ASC_UPLOAD_TIMEOUT_SECONDS` — pkg は約 56MB。回線が遅い環境では延ばす
   - `ASC_APP_ID` — `--app` の省略に使える
   - `ASC_KEY_TYPE` は既定の `team` のままでよい (今回の鍵は team キー)
7. **`asc` のバージョン差に注意** — リポジトリ配下で mise が activate されていると
   mise 版 (3.4.1)、リポジトリ外では Homebrew 版 (3.5.0) が引かれる。
   挙動が怪しいときは `which asc` で確認する。CI では mise 版に固定されるが、
   確実にしたいなら絶対パス指定 (`ASC_BIN`) を使う。

### 残課題

- **arm64 専用ビルドに残る x86_64 スライス** — `libbass.dylib` /
  `libSkiaSharp.dylib` / `libHarfBuzzSharp.dylib` / `libAvaloniaNative.dylib` は
  元から fat binary のため、arm64 専用ビルドでも x86_64 スライスを含んだまま
  配布される。検証は通るが約 20MB の無駄。`lipo -thin arm64` で削れる。
- **launchservicesd の temporary exception を外せるようにする** —
  「遭遇したエラー 6」の回避策。審査で問われる可能性があるため、本来は
  Avalonia 側で初期化経路を直すべき。試す価値がある方向:
  `src/KyoshinEewViewer.Desktop/Program.cs` の
  `MacOSPlatformOptions { DisableAvaloniaAppDelegate = true }` を外すと
  Avalonia の NSApplicationDelegate が入り、初期化順序が変わって
  lookup が不要になる可能性がある (未検証。この設定を外すと通知・トレイ周りの
  挙動が変わるため、外す場合は macOS 上での動作確認が必須)。
- **App Sandbox 下での動作検証** — 起動してプロセスが生存することまでは
  確認したが、設定ファイル (`~/Library/Application Support` はコンテナ内へ
  リダイレクトされる) や音声再生 (ManagedBass)、通知、地図タイルのキャッシュが
  サンドボックス内で正しく動くかは未検証。
  **TestFlight での処理完了 (VALID) はアプリが起動することすら保証しない**
  (build 2 が実例)。
- **`Contents/MacOS` にマネージド `.dll` やリソースが同居する構成** — Apple の
  バンドル構造ガイドラインからは外れるが、.NET の標準的な配置。将来
  検証で弾かれた場合は `Contents/Resources` や `Contents/Frameworks` への
  再配置を検討する。
- **TestFlight のテスターグループへの配布** — 本手順は ASC 上で VALID に
  なるまで。実際にテスターへ配るには `asc` のテスターグループ割り当てが別途必要。
