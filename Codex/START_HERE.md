# Revit MCP スタートアップガイド（作業開始用）

この文書は、RevitMCP の作業を開始するための最短導線です。ここから各ドキュメント/スクリプトに移動し、即時に作業を始められます。

Read First: `READ_FIRST_RevitMCP_JA.md` (one-page quickstart)
## 1) まず最初に（接続の確認）
- ポート確認（例: 5210）:
  - PowerShell: `Test-NetConnection localhost -Port 5210` が True であること
  - 環境変数でも指定可能: `$env:REVIT_MCP_PORT = 5210`
- クイックスタート手順:
  - Manuals/ConnectionGuide/QUICKSTART.md を参照
  - ping と bootstrap を自動実行するスクリプト: `Manuals/Scripts/test_connection.ps1`

## 2) 作業フォルダ（Work）とプロジェクトの準備
- 作業ルール: `WORK_RULES.md` を参照（セッション開始時に必読）
- Work 配下にプロジェクト専用フォルダを作成（命名: `<ProjectName>_<ProjectID>`）し、その中で作業を行ってください。
  - 例: `Work/SampleBuilding_P-2025-01`
  - 既に `Work/<ProjectName>_<ProjectID>/README.md` がある場合は、そこに作業メモ・成果物パスを追記
  - 【重要な基本ルール】`Work` 直下にファイル（`Work/*.json` など）を直接置かないでください。必ず `Work/<ProjectName>_<ProjectID>/...` 配下に保存・出力します。
起動ごとのチェック（必須）
- Revit のポートと Work プロジェクトフォルダの対応を確認（`<ProjectName>_<ProjectID or Port>`）。
- `Manuals/Scripts/test_connection.ps1 -Port <PORT>` を実行し、`Work/<Project>_<Port>/Logs/agent_bootstrap.json` を最新化。
- `list_commands (namesOnly:true)` を `Work/<Project>_<Port>/Logs/list_commands_names.json` に保存し、マニュアルとの差分がないか確認。

## 3) 要素取得（読み取り）
- アクティブビューの要素ID一覧を取得:
  - `Manuals/Scripts/list_elements_in_view.ps1 -Port <PORT>`
  - 自動で `Work/<ProjectName>_<ProjectID>/Logs/elements_in_view.json` を保存（JSON-RPC エンベロープのため値は `result.result.*` 以下）
- よく使うコマンド例: `Manuals/Commands/HOT_COMMANDS.md`

## 4) 書き込み（安全な2段階実行）
- 書込みは必ず安全版スクリプト（smoke_test → 実行）を使用:
  - 表示上書き（赤・60% 透明）
    - 事前確認のみ: `Manuals/Scripts/set_visual_override_safe.ps1 -DryRun`
    - 実行: `Manuals/Scripts/set_visual_override_safe.ps1 -ElementId <id>`
  - パラメータ更新（例: Comments）
    - 事前確認のみ: `Manuals/Scripts/update_wall_parameter_safe.ps1 -Param Comments -Value "Test" -DryRun`
    - 実行: `Manuals/Scripts/update_wall_parameter_safe.ps1 -ElementId <id> -Param Comments -Value "Test"`
- 注意: `viewId: 0` や `elementId: 0` は絶対に送らないでください（安全版は送信前に検知して停止します）

## 5) 参照ドキュメント（入口）
- エージェント向け統合ガイド: `Manuals/AGENT_README.md`
- 接続ガイド（索引）: `Manuals/ConnectionGuide/INDEX.md`
- 接続クイックスタート: `Manuals/ConnectionGuide/QUICKSTART.md`
- コマンドの全体像: `Manuals/Commands/commands_index.json`（正規リストは Archive 内の原本も参照）
- スクリプト一覧・使い方: `Manuals/Scripts/README.md`
- プリマー（JSONL）: `Manuals/ConnectionGuide/PRIMER.jsonl`

## 6) 進捗・記録
- 主要な補助スクリプトは `Work/<ProjectName>_<ProjectID>/Logs/*.json` に結果を保存します。
- リポジトリの履歴用: `Manuals/PROGRESS.jsonl` に自動でイベントを追記
- さらに細かい作業メモは、各プロジェクトの `Work/<ProjectName>_<ProjectID>/README.md` に追記してください。

## 7) トラブルシュート（要点）
- ポートに到達できない: Revit を起動し MCP Add-in の待受を確認。`Test-NetConnection` が True であること。
- 書込みがタイムアウト: 対象要素を減らす/ビュー権限やテンプレートを確認/待ち時間を延長（スクリプトは 120s 目安）。
- `smoke_test` が利用不可: 読み取り中心に手順を進め、Add-in 更新後に安全版スクリプトで再試行。

---
この START_HERE.md を入口にすれば、Manuals 配下のガイド・スクリプト・コマンド索引に素早く到達し、Work 配下のプロジェクトフォルダで直ちに作業を開始できます。

