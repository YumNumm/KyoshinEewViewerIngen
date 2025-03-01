# ワークフロー機能の使い方

ワークフローは、既存の通知･音声再生･イベントフック機能を置き換えるべく作成された、自由度の高い機能です。

むやみに乱用すると正常に動作しなかったり、アプリケーションやPC全体が不安定になる可能性があります。  
十分注意してご利用ください。

> [!WARNING]
> ワークフロー機能は開発中の機能です。
> バージョンアップでイベントのデータが変更されたり、廃止される可能性があります。
> アップデートの際は内容をよくご確認の上アップデートをお願いいたします。

## セーブ機能について

この機能は既存の設定ファイル( `config.json` )とは別のファイル( `workflows.json` )に保存されます。  
削除などの動作が簡単にできてしまうことから**自動でファイルへの保存は行いません**。  
こまめに保存やバックアップをお願いいたします。

## 概念

ワークフロー機能には主に4つの概念が登場します。

### ワークフロー

トリガーとアクションをまとめた一連の定義です。  
複数作成することができ、有効無効の切り替えや、任意の名前を付けることができます。

### トリガー

アクションを実行する条件を定義します。  
ワークフローにつき原則一つのみです。  
テスト実行機能も備えており、指定した条件に合致するテストイベントを生成する機能があります。

### アクション

トリガーで設定された条件を満たしている場合に実行される挙動を定義します。  
ワークフローにつき原則1つのみですが、後述する複数アクションを実行するアクションにより、複数のアクションを実行させることもできます。

### イベント

トリガーで定義された条件に合致したときに、アクション実行時に付加される情報です。  
テンプレート等で利用できるほか、アクションによっては環境変数やペイロードなどから参照できます。

## テンプレートについて

テンプレートエンジンとして [Scriban](https://github.com/scriban/scriban) を使用します。  
[条件分岐などの書き方についてはこちら](https://github.com/scriban/scriban/blob/master/doc/language.md)も参考にしてみてください。

## トリガー+イベント解説

### 何もしない

テスト実行以外では何も実行されないトリガーです。

| 名前        | 型　       | 解説　            | 例　                                     |
|:----------|:---------|:---------------|:---------------------------------------|
| EventType | string   | イベント区別のための固定値  | `Test`                                 |
| EventId   | Guid     | イベント区別のためのUUID | `a5142d28-8c81-4179-acf7-1b2116791a10` |
| IsTest    | bool     | テストイベントかどうか    | `true`                                 |
| Time      | DateTime | テストボタンを押した時刻   | `2024-04-25T06:36:06.4406939+09:00`    |

### `0.19.3より利用可` すべて

すべてのイベントに対してアクションが実行されます。  
複雑な読み上げや Webhook などでの連携にご活用ください。

### `0.18.2より利用可` アプリケーション起動時

アプリ起動時に1回だけ実行されるトリガーです。

| 名前        | 型      | 解説             | 例                                      |
|:----------|:-------|:---------------|:---------------------------------------|
| EventType | string | イベント区別のための固定値  | `ApplicationStartup`                   |
| EventId   | Guid   | イベント区別のためのUUID | `a5142d28-8c81-4179-acf7-1b2116791a10` |
| IsTest    | bool   | テストイベントかどうか    | `true`                                 |

### `0.18.2より利用可` アプリケーションの更新存在時

アプリの更新が存在するときにトリガーされます。

> [!IMPORTANT]
> - `定期的に更新情報をチェックする` が有効でない場合は動作しません。

繰り返しトリガーする が有効の場合、アップデートチェックの度(おおよそ10分ごと)にアップデートが存在する場合トリガーされます。  
無効の場合アップデートが見つかった時に1回だけトリガーされます。

| 名前             | 型      | 解説                 | 例                                      |
|:---------------|:-------|:-------------------|:---------------------------------------|
| EventType      | string | イベント区別のための固定値      | `ApplicationStartup`                   |
| EventId        | Guid   | イベント区別のためのUUID     | `a5142d28-8c81-4179-acf7-1b2116791a10` |
| IsTest         | bool   | テストイベントかどうか        | `true`                                 |
| IsContinuous   | bool   | アップデート存在状態が継続しているか | `true`                                 |
| LatestVersion  | string | 最新のバージョン           | `v0.18.8`                              |

### (強震モニタ)揺れ検知

強震モニタタブで揺れを検知したときのトリガーです。

> [!IMPORTANT]
> - 強震モニタ タブが表示されていない場合は動作しません。
> - かつ、揺れの検出を有効にする オプションが有効でない場合も動作しません。

現状では検知開始、検知レベルの上昇でしかトリガーされませんが、拡充を予定しています。  
デフォルトでは 強い揺れ を検知した時には 弱い揺れ のトリガーでも動作しますが、 `レベルが一致しているときのみ実行する` を有効にすると、そのレベルの時にしか動作しなくなります。

#### データモデル

|名前|型|解説|例|
|:--|:--|:--|:--|
|EventType|string|イベント区別のための固定値|`KyoshinShakeDetected`|
|EventId|Guid|イベント区別のためのUUID|`a5142d28-8c81-4179-acf7-1b2116791a10`|
|IsTest|bool|テストイベントかどうか|`true`|
|EventedAt|DateTime|イベントが発生した強震モニタ上の時刻|`2024-04-25T06:36:06.4406939+09:00`|
|FirstEventedAt|DateTime|イベントが初めて発生した強震モニタ上の時刻|`2024-04-25T06:36:06.4406939+09:00`|
|KyoshinEventId|Guid|**揺れ検知イベント**区別のためのUUID|`a5142d28-8c81-4179-acf7-1b2116791a10`|
|Level|KyoshinEventLevel|イベントの揺れの強さ|`weak`|
|Regions|string[]|イベントに含まれている地域一覧||
|IsReplay (0.19.0 より利用可)|bool|リプレイ中(タイムシフト再生など)か|`false`|

#### KyoshinEventLevel

Jsonの場合先頭は小文字になります。

|名前|揺れの強さ|
|:--|:--|
|`Weaker`|微弱な揺れ|
|`Weak`|弱い揺れ(震度1未満)|
|`Medium`|揺れ(震度1以上)|
|`Strong`|強い揺れ(震度3程度以上)|
|`Stronger`|非常に強い揺れ(震度5弱程度以上)|

### `0.18.1より利用可` (強震モニタ)緊急地震速報

強震モニタタブで揺れを検知したときのトリガーです。

> [!IMPORTANT]
> - 強震モニタ タブが表示されていない場合は動作しません。
> - 緊急地震速報 設定の `詳細な情報を表示する` が無効になっている場合、UI 上での表示条件に合わせて一部の情報を受信してもワークフローがトリガーされません。  
>   受信したすべての緊急地震速報でトリガーさせたい場合は、 `詳細な情報を表示する` を有効にしてください。
> - 仮定震源要素の場合、マグニチュード1.0 深さ10km の固定値となり、座標も最初に検知した地震計の座標となるためご注意ください。

> [!CAUTION]
> - **強震モニタが配信している緊急地震速報はあくまで強震モニタと合わせて表示するための情報であり、Webhookなどを使用して外部で単体の情報として扱うことは[強震モニタの利用条件](https://www.kyoshin.bosai.go.jp/kyoshin/docs/new_kyoshinmonitor.shtml#kmoni_useterms)から逸脱した行為となります。**  
>   あくまでアプリの挙動のカスタマイズとして利用し、**情報を単体で利用しないでください**。
> - 個人向けのプラン契約での DM-D.S.S から受信した緊急地震速報の再配信は禁止となっています。  
>   詳細は DM-D.S.S 公式の[再配信ポリシー](https://dmdata.jp/docs/eew/#%E5%86%8D%E9%85%8D%E4%BF%A1%E3%83%9D%E3%83%AA%E3%82%B7%E3%83%BC)をご確認ください。
>
> あくまでアプリの範囲内で、節度を持ってご利用ください。

#### 条件(発表･更新)

情報自体の発表や更新によるトリガーの条件の指定です。

- 新規発表
  - アプリが新規の緊急地震速報を受信したときにトリガーされます。
  - 続報発表では発表されません。
- 続報発表
  - 続報が発表されたときにトリガーされます。
- より精度の高い情報ソースからの情報
  - 報数が更新されずにより精度の高い情報ソースから情報のみを更新したときにトリガーされます。
  - 例えば、 SignalNow で受信後に DM-D.S.S で情報が補完された場合などです。
- 最終報
  - 最終報を受信したときにトリガーされます。
- キャンセル報
  - キャンセル報を受信もしくは、強震モニタから受信できなくなりキャンセル報もしくは範囲外の深さになったときにトリガーされます。

#### 条件(変更等)

状態の変化によるトリガーの条件の指定です。  
必ず情報自体の発表や更新が同時に発生しています。

- 警報発表
  - 緊急地震速報(警報) 発表時に地震ごとに1度だけトリガーされます。
  - 現段階では警報の続報が出てもトリガーされません。
    - よって、**警報地域の更新検知の目的にこの条件を利用しないでください**。
- 予想最大震度上昇
  - 更新前の情報と比較して予想震度が上方修正された場合にトリガーされます。
  - 予想震度が不明だったものに震度が設定された場合にもトリガーされます。
- 予想最大震度低下
  - 更新前の情報と比較して予想震度が上方修正された場合にトリガーされます。
  - 予想震度が不明になった場合にもトリガーされます。

> [!WARNING]
> SignalNow Professional 連携を使用している場合、最大震度が取得できず不明扱いになる都合上、  
> **情報更新の度に `予想最大震度上昇` と `予想最大震度低下` の条件がトリガーされることになります**。  
> 素早い情報の取得に DM-D.S.S の利用もご検討ください。

### データモデル

|名前|型|解説|例|
|:--|:--|:--|:--|
|EventType|string|イベント区別のための固定値|`Eew`|
|EventId|Guid|イベント区別のためのUUID|`a5142d28-8c81-4179-acf7-1b2116791a10`|
|IsTest|bool|テストイベントかどうか|`true`|
|EventSubType|EewEventType|イベントの条件区別|`New`|
|EewId|string|緊急地震速報のイベントID|`20240430010203`|
|SerialNo|int|緊急地震速報の報数|`1`|
|OccurrenceAt|DateTime|地震の推定発生時刻|`2024-04-25T06:36:06.4406939+09:00`|
|EewSource|string|受信元|`強震モニタ`|
|IsTrueCancelled|bool|キャンセルであることが確定しているか<br>強震モニタ上でキャンセルもしくは受信範囲外とみなした場合は false|`true`|
|Intensity|JmaIntensity|最大震度|`Int6Upper`|
|IsIntensityOver|bool|最大震度が上記の震度程度以上かどうか|`true`|
|EpicenterPlaceName|string|震央地名|`石川県能登地方`|
|EpicenterLocation|Location|震央座標|`石川県能登地方`|
|Magnitude|float?|マグニチュード<br>データがない場合は null|`3.5`|
|Depth|int|震源の深さ(km)|`10`|
|IsTemporaryEpicenter|bool|仮定震源要素か|`false`|
|IsWarning|bool|警報状態か|`false`|
|WarningAreaCodes|string[]|警報地域コードの配列|`999`|
|WarningAreaNames|string[]|警報地域名の配列|`aaa`|
|IsFinal|bool|最終報か|`false`|
|IsCancelled|bool|キャンセル報か|`false`|
|IsReplay (0.18.13 より利用可)|bool|リプレイ中(タイムシフト再生など)か|`false`|

### EewEventType

Jsonの場合先頭は小文字になります。

|名前|サブタイプ|
|:--|:--|
|`New`|新規発表|
|`UpdateNewSerial`|続報発表|
|`UpdateWithMoreAccurate`|より精度の高い情報ソースからの情報|
|`Final`|最終報|
|`Cancel`|キャンセル報|
|`NewWarning`|警報発表|
|`IncreaseMaxIntensity`|予想最大震度上昇|
|`DecreaseMaxIntensity`|予想最大震度低下|

### JmaIntensity

Jsonの場合先頭は小文字になります。

|名前|震度|
|:--|:--|
|`Unknown`|不明|
|`Int0`|震度0|
|`Int1`|震度1|
|`Int2`|震度2|
|`Int3`|震度3|
|`Int4`|震度4|
|`Int5Lower`|震度5弱|
|`Int5Upper`|震度5強|
|`Int6Lower`|震度6弱|
|`Int6Upper`|震度6強|
|`Int7`|震度7|

### Location

|名前|型|解説|
|:--|:--|:--|
|Latitude|float|緯度|
|Longitude|float|経度|

### `0.18.4より利用可` (地震情報)地震情報受信

地震情報の受信･更新時にトリガーされます。

- `情報受信時` は情報の有無にかかわらず受信した時点でトリガーされます。
    - 各情報種別のチェックボックスで切り替えることができます。
    - 例えば、震度速報のみを受信したい場合は、`震度速報` 以外のチェックを外してください。
- `最大震度変更時` は新規に受信したときと、最大震度が変化した際にトリガーされます。
    - `震度が上昇したときのみ` を有効にすると、震度が下がった場合や最大震度が変わらな買った場合トリガーされません。
    - 長周期地震動階級は考慮されません。

> [!IMPORTANT]
> - 地震情報 タブが表示されていない場合は動作しません。
> - `Comment` は将来的に仕様が変更される可能性があります。
> - 内部構造の都合のため、観測地点の情報は現状ありません。要望がありましたらお伝えください。

### データモデル

|名前|型|解説|例|
|:--|:--|:--|:--|
|EventType|string|イベント区別のための固定値|`EarthquakeInformation`|
|EventId|Guid|イベント区別のためのUUID|`a5142d28-8c81-4179-acf7-1b2116791a10`|
|IsTest|bool|テストイベントかどうか|`true`|
|UpdatedAt|DateTime|地震情報の更新時刻|`2024-04-25T06:36:06`|
|LatestInformationName|string|最後に受信した地震情報の名前|`震度速報` `震源に関する情報` `震源・震度に関する情報` `顕著な地震の震源要素更新のお知らせ` `津波警報・注意報・予報a` `長周期地震動に関する観測情報`|
|EarthquakeId|string|地震情報のID|`20240425063606`|
|IsTrainingOrTest|bool|訓練かテストか|`false`|
|DetectedAt|DateTime?|揺れの検知時刻 震源情報が存在しない場合のみ|`2024-04-25T06:36:06`|
|MaxIntensity|JmaIntensity|最大震度|`int3`|
|PreviousMaxIntensity|JmaIntensity?|前回の最大震度 初回の場合は `null`|`int2`|
|MaxLpgmIntensity|LpgmIntensity?|最大長周期地震動階級 未受信の場合は `null`|`lpgmInt1`|
|Hypocenter|EarthquakeInformationEventHypocenter?|震源情報 震度速報など、存在しない場合は `null`||
|Comment|string|電文のコメント|`この地震による津波の心配はありません。`|
|FreeFormComment|string|電文の自由記述のコメント||
|IsVolcano (0.18.13 より利用可)|bool|大規模な噴火情報か|`true`|
|VolcanoName (0.18.13 より利用可)|string?|噴火名 上手く抽出できないことがあります|`レウォトビ火山`|

### EarthquakeInformationEventHypocenter

|名前|型|解説|
|:--|:--|:--|
|OccurrenceAt|DateTime|地震の発生時刻|
|PlaceName|string|震央地名|
|Location|Location|震央座標|
|Magnitude|float|マグニチュード|
|MagnitudeAlternativeText|string?|数値で規模が表せない場合の代替テキスト|
|Depth|int|震源の深さ(km)|
|IsForeign|bool|遠地地震か|

### LpgmIntensity

Jsonの場合先頭は小文字になります。

|名前|長周期地震動階級|
|:--|:--|
|`Unknown`|不明|
|`LpgmInt0`|階級0|
|`LpgmInt1`|階級1|
|`LpgmInt2`|階級2|
|`LpgmInt3`|階級3|
|`LpgmInt4`|階級4|

### `0.18.7より利用可` (津波情報)津波情報更新時

津波情報の受信･更新時にトリガーされます。

- 基本的には情報の更新にかかわらず、情報の受信時にトリガーされます。
- `警報種別が切り替えられたときのみ` を有効にすると、警報種別が変更されたときのみトリガーされます。

> [!IMPORTANT]
> - 津波情報 タブが表示されていない場合は動作しません。
> - `TsunamiInfo` は表示のためのモデルをそのまま流用しているため将来的に仕様が変更されます。
> - 内部構造の都合のため、観測高が文字列であるなどの制約があります。要望がありましたらお伝えください。

### データモデル

|名前|型|解説|例|
|:--|:--|:--|:--|
|EventType|string|イベント区別のための固定値|`TsunamiInformation`|
|EventId|Guid|イベント区別のためのUUID|`a5142d28-8c81-4179-acf7-1b2116791a10`|
|IsTest|bool|テストイベントかどうか|`true`|
|TsunamiInfo|TsunamiInfo|津波情報のデータ||
|Level|TsunamiLevel|津波警報の種別|`Warning`|
|PreviousLevel|TsunamiLevel?|前回の津波警報の種別|`Advisory`|

### TsunamiInfo

|名前|型|解説|
|:--|:--|:--|
|EventId|string|津波情報のID|
|SpecialState|string|電文の状態(訓練/試験)|
|ReportedAt|DateTime|津波情報の受信時刻|
|ExpireAt|DateTime?|津波予報の有効期限(津波予報でないときは `null`)|
|NoTsunamiAreas|TsunamiWarningArea[]?|津波の心配がない地域の情報(観測情報のみ存在する場合)|
|ForecastAreas|TsunamiWarningArea[]?|予報地域|
|AdvisoryAreas|TsunamiWarningArea[]?|注意報地域|
|WarningAreas|TsunamiWarningArea[]?|警報地域|
|MajorWarningAreas|TsunamiWarningArea[]?|大津波警報地域|

### TsunamiWarningArea

|名前|型|解説|
|:--|:--|:--|
|Code|int|地域コード|
|Name|string|地域名|
|Height|string|予想高|
|State|string|到達状況|
|ArrivalTime|DateTime|内部でのソートに利用する到達時刻|
|Stations|TsunamiStation[]?|観測地点の情報|

### TsunamiStation

確実に仕様を変更する予定なのでちょっと割愛させてください…。

### TsunamiLevel

Jsonの場合先頭は小文字になります。

|名前|津波情報の種別|
|:--|:--|
|`None`|なし|
|`Forecast`|津波予報|
|`Advisory`|津波注意報|
|`Warning`|津波警報|
|`MajorWarning`|大津波警報|

## アクション解説

### 何もしない

本当に何もしません。

### 複数アクション実行

複数のアクションを連続もしくは並列して実行することができます。  
複雑にはなりますが、複数アクション実行を入れ子にすることもできます。

`同時実行を行う` を有効にすると各アクションを同時に実行しようとしますが、環境によってはアプリケーションの他の機能に影響を及ぼすこともありますのでご注意ください。

### 通知送信

通知を指定した文言で送信できます。  
通知アイコンなどが無効になっている場合は何も起こりません。

本文はテンプレートが利用できますが、タイトルはテンプレートは利用できません。

### 音声再生

テンプレートで指定したファイルを再生することができます。  
`待機` を有効にすると複数アクション実行時にファイルの再生が終わるまで次のアクションの動作をブロックすることができます。

### メインウィンドウを最前面に表示

文字通り、メインウィンドウを最前面に表示します。

### 指定時間待機

複数アクション実行時に次のアクションの動作を遅らせることができます。  
もちろんですが、同時実行を行っているときには意味がありません。

### ログ出力

テンプレートで指定した内容をログに出力します。  
ログのファイル出力が有効になっていればファイルにも出力されますので、デバッグや動作状況の確認などにご利用ください。

### 指定したURLに内容をPOST

指定した URL にイベントの内容を Json にしたものを POST します。(所謂 Webhook)  
今のところリクエスト送信中は次のアクションの実行はブロックされます。

### 指定したファイルを開く(実行)

指定したファイルを開こうとします。

`既定のアプリで開く` が有効の場合はシェル経由で、無効の場合は直接起動しようとします。

`プロセス終了を待機する` が有効の場合はプロセスが終了するまで次のアクションの実行をブロックします。  
ファイルを開こうとした場合は大抵表示等するアプリケーションが起動する時点までになりますのでご注意ください。

### `0.18.8より利用可` VOICEVOX でテキスト読み上げ

指定したテキストを [VOICEVOX](https://voicevox.hiroshiba.jp/) で読み上げます。  
`読み上げ終了まで待機する` を有効にすると複数アクション実行時に読み上げが終わるまで次のアクションの動作をブロックすることができます。

## 利用例

### 揺れ検知時に地域を通知

![image](https://github.com/ingen084/KyoshinEewViewerIngen/assets/5910959/82304d2f-c60f-4ee4-8f0b-501d041144b3)

```scriban
{{
  for r in Regions
    r + " "
  end
  "で"
  case Level
    when "Weak"
      "弱い"
    when "Medium"
      ""
    when "Strong"
      "強い"
    when "Stronger"
      "非常に強い"
    else
      Level
  end
}}揺れを検知しました
```
