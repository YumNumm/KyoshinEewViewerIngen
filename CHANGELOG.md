# Changelog

## [1.0.9](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/1.0.8...1.0.9) - 2026-08-23

- fix: exclude local artifacts from Docker context by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/27
- ci: iOS ビルドを TestFlight へ配信する deploy-app ワークフローを追加 by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/30
- feat: 画面幅が狭い端末でメイン画面と設定画面をハンバーガーメニューにする by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/31
- feat: iOS / iPadOS 向けビルドを追加 by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/29
- fix: macOS/Linux の自己更新が自身の差し替えでクラッシュする問題を修正 by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/33
- Develop avalonia12 by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/32
- fix: Bundle ID を発行済みの net.yumnumm.KyoshinEewViewer に合わせる by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/36
- feat: iOS の通知と通知権限の要求を実装する by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/38
- 地震情報を縦向き/狭い画面向けのデザインに改善する by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/39
- feat: iOS の DMDATA 認可を専用クライアントとコールバック URL に切り替える by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/40
- fix: iOS で成立しない機能をまとめて無効化・固定する by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/41
- chore(deps): bump the github-actions group with 9 updates by @dependabot[bot] in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/34
- [ImgBot] Optimize images by @imgbot[bot] in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/35
- Bump FluentAvaloniaUI and 16 others by @dependabot[bot] in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/37
- feat: Metal HUD 風のパフォーマンスオーバーレイを追加 by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/42
- feat: iOS で AVFoundation を使って音声を再生できるようにする by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/43
- feat: Android ヘッドのビルドを復活させ AAB / APK を CI で出す by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/44
- feat: EQMonitor API から地震情報と緊急地震速報を取得する by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/45
- feat: EQMonitor API へ送る識別ヘッダを EQMonitor の形式に合わせる by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/46
- feat: 気象庁防災情報XMLの受信を無効にできるようにする by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/47
- feat: 地震履歴のページングと EQMonitor 由来の詳細表示に対応する by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/48
- fix: iOS/Android でウィンドウテーマの選択が保存されない問題を修正する by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/50
- feat: EQMonitor API で地震履歴を検索できるようにする by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/51
- feat: ネットワーク通信を追跡するデバッグ機能を追加する by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/52
- EEW履歴の実装 by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/53
- fix: 狭い画面の地震情報を左上に寄せて余白と文字を詰める by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/54
- ci: develop の iOS ビルドを TestFlight 外部テストへ配布する by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/55
- feat: EQMonitor API の既定の接続先をビルド時に埋め込み既定で有効にする by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/56
- ci: upstream同期Agentic Workflowを追加する by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/57
- fix: EEW履歴リプレイの時刻ずれを修正しP･S波到達予想円を表示する by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/58
- build(deps): bump the github-actions group with 3 updates by @dependabot[bot] in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/59
- シート表示中に左下のボタンを隠し、モバイルでは小さく表示する by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/61
- ci: CD の Desktop ビルドを Windows / Linux の x64・arm64 へ拡張する by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/63
- Bump the nuget group with 21 updates (ReactiveUI.Avalonia/Splat は据え置き) by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/62
- build(deps): bump github/gh-aw-actions/setup from 0.85.1 to 0.86.1 in the github-actions group by @dependabot[bot] in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/64
- Merge upstream/develop into develop by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/67
- EQMonitorの更新をWebSocketへ移行 by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/69
- build(deps): bump the github-actions group across 1 directory with 2 updates by @dependabot[bot] in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/68

## [1.0.8](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/1.0.7...1.0.8) - 2026-07-27
- fix(shake-detection-producer): Valkey publish時刻にJSTタイムゾーンを付与 by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/25

## [1.0.7](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/1.0.6...1.0.7) - 2026-07-24
- fix: 揺れ検知Snapshotの更新時刻を正規化 by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/22

## [1.0.6](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/1.0.5...1.0.6) - 2026-07-19
- feat: publish canonical shake detection state payloads by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/19

## [1.0.5](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/1.0.4...1.0.5) - 2026-06-04

- chore: PR は YumNumm（origin）のみ — Cursor ルールと CLAUDE 追記 by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/13
- Bump the nuget group with 1 update by @dependabot[bot] in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/14
- fix(ReplayGenerator): アクティブセッションがない場合は snapshot fetch をスキップ by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/16

## [1.0.4](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/1.0.3...1.0.4) - 2026-03-31

- Bump the nuget group with 1 update by @dependabot[bot] in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/11

## [1.0.3](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/1.0.2...1.0.3) - 2026-03-31

- feat: EqMonitorEewReplayData + ReplayGenerator Worker Service by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/8

## [1.0.2](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/1.0.1...1.0.2) - 2026-03-30

## [1.0.1](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/1.0.0...1.0.1) - 2026-01-25

## [1.0.0](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/0.13.11...1.0.0) - 2026-01-09

- 過度なAvalonia依存を削除 by @ingen084 in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/4
- upstream取り込み by @YumNumm in https://github.com/YumNumm/KyoshinEewViewerIngen/pull/5

## [0.13.11](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/0.13.10...0.13.11) - 2023-02-12

## [0.13.10](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/0.13.9...0.13.10) - 2023-01-28

## [0.13.9](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/0.13.8...0.13.9) - 2022-12-25

## [0.13.8](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/0.13.7...0.13.8) - 2022-12-12

## [0.13.7](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/0.13.6...0.13.7) - 2022-12-12

## [0.13.6](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/0.13.5...0.13.6) - 2022-11-23

## [0.13.5](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/0.13.4...0.13.5) - 2022-11-22

## [0.13.4](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/0.13.3...0.13.4) - 2022-11-21

## [0.13.3](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/0.13.2...0.13.3) - 2022-11-18

## [0.13.2](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/0.13.1...0.13.2) - 2022-10-28

## [0.13.1](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/0.13.0...0.13.1) - 2022-10-20

## [0.13.0](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/0.12.15...0.13.0) - 2022-09-15

## [0.12.15](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/0.12.14...0.12.15) - 2022-09-10

## [0.12.14](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/0.12.13...0.12.14) - 2022-09-09

## [0.12.13](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/0.12.12...0.12.13) - 2022-08-31

## [0.12.12](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/0.12.11...0.12.12) - 2022-08-31

## [0.12.11](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/0.12.10...0.12.11) - 2022-08-21

## [0.12.10](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/0.12.9...0.12.10) - 2022-08-20

## [0.12.9](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/0.12.8...0.12.9) - 2022-08-19

## [0.12.8](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/0.12.7...0.12.8) - 2022-08-06

## [0.12.7](https://github.com/YumNumm/KyoshinEewViewerIngen/compare/0.12.6...0.12.7) - 2022-07-29
