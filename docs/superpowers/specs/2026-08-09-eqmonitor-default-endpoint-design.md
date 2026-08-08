# EQMonitor API の既定接続先をビルド時に埋め込む

作成日: 2026-08-09

## 背景と目的

現在、EQMonitor API の接続先 (`EqMonitorConfig.BaseUrl`) は既定値を持たず、利用者が設定画面で入力しない限り利用できない。また `EqMonitorConfig.Enable` の既定値は `false` のため、配布物をインストールしただけでは EQMonitor API が一切動かない。

CD で配布するビルドについては、既定の接続先をビルド時に埋め込み、EQMonitor API を最初から有効にする。ただし接続先のドメイン名はリポジトリ・PR・アプリの UI・ログのいずれにも現れてはならない。

## 用語

- **ビルド時定数**: CD のビルド時に環境変数から読み取り、アセンブリへ埋め込む文字列
- **組み込み既定値 (`BuiltInBaseUrl`)**: 上記で埋め込まれた接続先。ローカルビルドでは `null`

## 要件

1. CD の配布ビルドでは EQMonitor API の接続先がビルド時に埋め込まれ、利用者の入力なしで通信できる
2. 接続先のドメイン名はリポジトリのソース・設定ファイル・PR 本文・アプリの画面・ログのいずれにも出さない
3. 利用者が設定画面で接続先を入力した場合はそちらを優先する
4. `EqMonitorConfig.Enable` の既定値を `true` にする（既存ユーザー向けの移行処理は行わない）
5. 結合テスト用ビルドには接続先を埋め込まない

## 設計

### 1. ビルド時定数の注入

接続先の値は **GitHub Secret `EQMONITOR_BASE_URL`** に格納し、CD のビルド step で環境変数として渡す。MSBuild は環境変数を自動的にプロパティとして解決するため、`dotnet publish` のコマンドラインや publish スクリプトへの引数追加は不要。既存の `APP_VERSION` / `BUILD_NUMBER` / `VERSION_SUFFIX` と同じ流儀に揃う。

`src/KyoshinEewViewer/KyoshinEewViewer.csproj` に以下を追加する。

```xml
<!-- 配布ビルドの既定接続先。値はリポジトリに持たず CD の Secret から環境変数で受け取る -->
<ItemGroup Condition="'$(EQMONITOR_BASE_URL)' != ''">
  <AssemblyAttribute Include="System.Reflection.AssemblyMetadataAttribute">
    <_Parameter1>EqMonitorBaseUrl</_Parameter1>
    <_Parameter2>$(EQMONITOR_BASE_URL)</_Parameter2>
  </AssemblyAttribute>
</ItemGroup>
```

環境変数が未設定のローカルビルドでは属性そのものが付与されないため、`BuiltInBaseUrl` は `null` になる。

**トリミングとの関係**: `common.props` は `PublishTrimmed=true` かつ `TrimMode=partial` であり、`IsTrimmable` 属性を持つアセンブリのみがトリム対象になる。`KyoshinEewViewer` 本体アセンブリは `IsTrimmable` ではないため、アセンブリレベルのカスタム属性が ILLink に削除されることはない。

### 2. 実行時の接続先解決

`src/KyoshinEewViewer/Services/EqMonitor/EqMonitorApiProvider.cs` を変更する。

```csharp
/// <summary>
/// 配布ビルドに埋め込まれた既定の接続先。埋め込まれていない場合は null
/// </summary>
public static string? BuiltInBaseUrl { get; } = Assembly.GetExecutingAssembly()
    .GetCustomAttributes<AssemblyMetadataAttribute>()
    .FirstOrDefault(a => a.Key == "EqMonitorBaseUrl")?.Value;
```

接続先の解決ロジックはテスト可能にするため静的メソッドへ切り出す。

```csharp
/// <summary>
/// 利用する接続先を決定する。設定値が空なら組み込み既定値へフォールバックする
/// </summary>
/// <returns>利用できる接続先がない場合は null</returns>
internal static Uri? ResolveBaseUri(string? configured, string? builtIn)
```

解決順と判定:

1. `configured` を trim して空でなければそれを使う
2. 空なら `builtIn` を使う
3. どちらも空なら `null`
4. 末尾がスラッシュでなければ付与する（`HttpClient.BaseAddress` が最後のパス要素を落とすため。既存の挙動を維持）
5. 絶対 URI として解釈でき、スキームが `http` / `https` のいずれかであること。満たさなければ `null`

`GetClient()` はこのメソッドの戻り値を使い、`null` なら従来どおりクライアントを提供しない。`_appliedBaseUrl` による使い回し判定は解決後の URI 文字列で行う。

現状 `GetClient()` は不正な接続先のときに `Logger.LogWarning("EQMonitor API の接続先が正しくないため利用できません")` を出しているが、この警告は利用者が入力した値が不正な場合にのみ意味がある。組み込み既定値へフォールバックした結果が不正になるのは開発時のミスなので、メッセージは変更しない（いずれも URL を含まないため要件 2 を満たす）。

### 3. 設定画面

接続先の入力欄は従来どおり常に表示し、上書き可能なままとする（画面構成は変更しない）。

`src/KyoshinEewViewer/Services/EqMonitor/EqMonitorPage.axaml` の `PlaceholderText` のみ、組み込み既定値の有無で切り替える。`EqMonitorSettingPage` に読み取り専用プロパティを追加してバインドする。

```csharp
/// <summary>
/// ベース URL 入力欄のプレースホルダ。接続先そのものは表示しない
/// </summary>
public string BaseUrlPlaceholder =>
    EqMonitorApiProvider.BuiltInBaseUrl is null ? "https://example.com" : "既定の接続先を使用します";
```

配布ビルドでは入力欄が空でも「既定の接続先を使用します」と表示され、利用者は通信が成立している理由を理解できる。ドメイン名は表示しない。

### 4. 既定で有効化

`src/KyoshinEewViewer.Core/Models/KyoshinEewViewerConfiguration.cs` の `EqMonitorConfig.Enable` の初期化子を `true` に変更する。

```csharp
private bool _enable = true;
```

既存ユーザーの `config.json` には `"Enable": false` が保存済みのため、この変更は新規インストール（および設定ファイルが無い状態での起動）にのみ効く。既存ユーザーを強制的に有効化する移行処理は行わない。

`EnableEarthquake` は既に既定 `true`、`EnableEew` は既定 `false` のまま変更しない（EEW はポーリング取得のため他の受信元より遅延する）。JMA XML など他の受信元の既定値にも手を入れない。

### 5. CD への配線

`.github/workflows/deploy-app.yaml` の配布ビルドを行う 4 つの job のビルド step に環境変数を追加する。

| job | step |
| --- | --- |
| `desktop-macos` | `Build` |
| `android` | `Build AAB / APK` |
| `build-ios` | `Build ipa` |
| `build-macos` | `Build pkg` |

```yaml
env:
  EQMONITOR_BASE_URL: ${{ secrets.EQMONITOR_BASE_URL }}
```

`build-ios` / `build-macos` は publish 処理を `scripts/publish-testflight-*.sh` に委譲しているが、環境変数はプロセスに継承されるためスクリプト側の変更は不要。

`.github/workflows/integration-test.yml` には追加しない（要件 5）。fork からの pull_request では Secret が渡らないため、その場合も接続先は埋め込まれない。

**Secret を選ぶ理由**: 本リポジトリは公開リポジトリで Actions のログも公開される。GitHub Variables は値がログへ出得るのに対し、Secret はマスクされる。埋め込まれた文字列自体は配布バイナリから読み取れるため機密ではないが、要件 2 を機械的に担保する手段として Secret を用いる。

**運用上の前提**: CD 実行前にリポジトリの Secret `EQMONITOR_BASE_URL` を登録しておく必要がある。未登録の場合は従来どおり接続先が埋め込まれないだけで、ビルドは成功する。

## テスト

`tests/KyoshinEewViewer.Tests/Services/EqMonitorApiClientTests.cs` に `ResolveBaseUri` のテストを追加する。`KyoshinEewViewer` は `InternalsVisibleTo` でテストプロジェクトへ公開済み。

検証する分岐:

| 設定値 | 組み込み | 期待 |
| --- | --- | --- |
| `https://configuredexample/` | `https://builtinexample/` | 設定値 |
| 空文字 / 空白のみ | `https://builtinexample/` | 組み込み |
| 空文字 | `null` | `null` |
| `https://configuredexample/v2`（末尾スラッシュ無し） | `null` | `https://configuredexample/v2/` |
| `ftp://configuredexample/` | `null` | `null`（スキーム不正） |
| `not-a-url` | `null` | `null` |

テストデータには CLAUDE.md の規約に従い実在しないホスト名のみを用いる。

`Enable` の既定値変更と `PlaceholderText` の切り替えは単純なプロパティであり、CLAUDE.md の「テストしないもの」に該当するためテストを追加しない。

## 影響範囲

| ファイル | 変更 |
| --- | --- |
| `src/KyoshinEewViewer/KyoshinEewViewer.csproj` | `AssemblyMetadataAttribute` の条件付き付与 |
| `src/KyoshinEewViewer/Services/EqMonitor/EqMonitorApiProvider.cs` | `BuiltInBaseUrl` 追加、`ResolveBaseUri` 切り出し、`GetClient()` の書き換え |
| `src/KyoshinEewViewer/Services/EqMonitor/EqMonitorSettingPage.cs` | `BaseUrlPlaceholder` 追加 |
| `src/KyoshinEewViewer/Services/EqMonitor/EqMonitorPage.axaml` | `PlaceholderText` のバインド |
| `src/KyoshinEewViewer.Core/Models/KyoshinEewViewerConfiguration.cs` | `EqMonitorConfig.Enable` の既定値 |
| `.github/workflows/deploy-app.yaml` | 4 job への `env` 追加 |
| `tests/KyoshinEewViewer.Tests/Services/EqMonitorApiClientTests.cs` | `ResolveBaseUri` のテスト |

## スコープ外

- 既存ユーザーの `Enable` を移行処理で有効化すること
- `EnableEew` の既定値変更
- JMA XML / Dmdata など他の受信元の既定値変更
- Windows / Linux 向け配布ビルド（現在の `deploy-app.yaml` は生成していない）
