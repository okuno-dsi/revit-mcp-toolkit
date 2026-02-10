# Revit MCP スタートアップガイド（作業開始用）

この文書は、RevitMCP の作業を開始するための最短導線です。ここから各ドキュメント/スクリプトに移動し、即時に作業を始められます。

Read First: `READ_FIRST_RevitMCP_JA.md` (one-page quickstart)
**必読**: `WORK_RULES.md`（Work の運用ルール。プロジェクトごとの保存先を必ず遵守）
## 1) まず最初に（接続の確認）
- ポート確認（例: 5210）:
  - PowerShell: `Test-NetConnection localhost -Port 5210` が True であること
  - 環境変数でも指定可能: `$env:REVIT_MCP_PORT = 5210`
- クイックスタート手順:
  - Docs/Manuals/ConnectionGuide/QUICKSTART.md を参照
  - ping と bootstrap を自動実行するスクリプト: `Scripts/Reference/test_connection.ps1`

## 2) 作業フォルダ（Work）とプロジェクトの準備
- 作業ルール: `WORK_RULES.md` を参照（セッション開始時に必読）
- **Projects の実体は `C:\Users\<user>\Documents\Revit_MCP\Projects`（paths.json の `workRoot`）です。**
- `Projects/` と記載されている場合は、必ず **上記の絶対パス配下** を指します（`Revit_MCP\Codex\Projects` ではありません）。
- Work 配下にプロジェクト専用フォルダを作成（命名: `<RevitFileName>_<docKey>`）し、その中で作業を行ってください。
  - 例: `Projects/ProjectA_09cddb07-4f24-4876-b15d-93d4af125bcb`
  - すべての取得データ・中間ファイル・スクリプトはこの配下に保存します（例: `python_script`）。
  - 既に `Projects/<ProjectName>_<ProjectID>/README.md` がある場合は、そこに作業メモ・成果物パスを追記
  - 【重要な基本ルール】`Work` 直下にファイル（`Projects/*.json` など）を直接置かないでください。必ず `Projects/<ProjectName>_<ProjectID>/...` 配下に保存・出力します。
起動ごとのチェック（必須）
- Revit のポートと Work プロジェクトフォルダの対応を確認（`<ProjectName>_<ProjectID or Port>`）。
- `Scripts/Reference/test_connection.ps1 -Port <PORT>` を実行し、`Projects/<Project>_<Port>/Logs/agent_bootstrap.json` を最新化。
- `list_commands (namesOnly:true)` を `Projects/<Project>_<Port>/Logs/list_commands_names.json` に保存し、マニュアルとの差分がないか確認。

## 3) 要素取得（読み取り）
- アクティブビューの要素ID一覧を取得:
  - `Scripts/Reference/list_elements_in_view.ps1 -Port <PORT>`
  - 自動で `Projects/<ProjectName>_<ProjectID>/Logs/elements_in_view.json` を保存（JSON-RPC エンベロープのため値は `result.result.*` 以下）
- よく使うコマンド例: `Docs/Manuals/Commands/HOT_COMMANDS.md`

## 4) 書き込み（安全な2段階実行）
- 書込みは必ず安全版スクリプト（smoke_test → 実行）を使用:
  - 表示上書き（赤・60% 透明）
    - 事前確認のみ: `Scripts/Reference/set_visual_override_safe.ps1 -DryRun`
    - 実行: `Scripts/Reference/set_visual_override_safe.ps1 -ElementId <id>`
  - パラメータ更新（例: Comments）
    - 事前確認のみ: `Scripts/Reference/update_wall_parameter_safe.ps1 -Param Comments -Value "Test" -DryRun`
    - 実行: `Scripts/Reference/update_wall_parameter_safe.ps1 -ElementId <id> -Param Comments -Value "Test"`
- 注意: `viewId: 0` や `elementId: 0` は絶対に送らないでください（安全版は送信前に検知して停止します）

## 4.5) TaskSpec v2（推奨: 複雑/Write の場合）
**原則**: AI（エージェント）は「自由に Python/PowerShell を生成して叩く」のではなく、まず **TaskSpec（宣言的JSON）** を作成し、固定ランナーで実行します（事前検証でミスとやり直しを減らす）。

### AI が TaskSpec を使う判断（推奨ルール）
- **TaskSpec 必須**:
  - Revitモデルを変更する（Write）/ 破壊的操作（delete 等）/ `Risk=Medium/High`
  - 複数ステップ（2コマンド以上）・条件分岐・結果を次の呼び出しに渡す
  - 処理対象が **5要素以上**、または大量要素になり得る
  - タイムアウト懸念、failureHandling の確認が必要、ロールバック方針を明示したい
- **直叩き（従来の1回実行）でもOK**:
  - 単発の Read（例: `revit.status`, `help.get_context` など）で、パラメータが確定しており副作用がない
  - 失敗しても再実行が安全で、結果が次の処理に連鎖しない

### 置き場所（重要）
- TaskSpec は `Projects/<ProjectName>_<ProjectID>/Tasks/*.task.json` に保存します（`Work` 直下は禁止）。

### 実行（固定ランナー）
- TaskSpec の `server`（RevitMCP 推奨）: `http://127.0.0.1:<PORT>/enqueue`（または `http://127.0.0.1:<PORT>`）
- Python: `python Docs/Manuals/Design/taskspec-v2-kit/runner/mcp_task_runner_v2.py <task.json> --dry-run`（送信前検証）
- Python: `python Docs/Manuals/Design/taskspec-v2-kit/runner/mcp_task_runner_v2.py <task.json>`（実行）
- PowerShell: `pwsh -ExecutionPolicy Bypass -File Docs/Manuals/Design/taskspec-v2-kit/runner/mcp_task_runner_v2.ps1 -Task <task.json> -DryRun`
- 実行ログ: Task が `Projects/<Project>/Tasks` 配下なら、自動で `Projects/<Project>/Logs` に JSONL を出力（`--out` / `-Out` で上書き可）

## 4.6) Collaborative Chat（チーム作業のためのチャット）
Revit の作業共有チーム向けに、**人間が主役**の軽量チャット（ログが正）を追加しました。

- Revit 側（非AIユーザーもOK）
  - リボン: `RevitMCPServer` タブ → `GUI` パネル → `Chat` ボタンでチャットペインを表示/非表示
  - 送信: `ws://Project/General`（既定）にメッセージ送信
  - 招待: `Invite` → ユーザーID（Revit Username想定）を入力 → `ws://Project/Invites` に招待を投稿
  - 受信: 自分宛の `@UserId`（招待）を検出すると、Revit作業を妨げないトースト通知を表示

- AI 側（Codex GUI 推奨）
  - Codex GUI の上部 `Chat` ボタン → Chat Monitor を開く（`chat.list` をポーリング）
  - Chat Monitor は read-only（コンプライアンス）: AI を自動実行しません。また、AIの結果をチャットへ自動投稿（`chat.post`）もしません。
  - 代替: 対象メッセージを選択 → `Copy→Codex`（本文を Codex GUI の入力欄へ貼り付け）→ ユーザーが `Send` を手動実行

- 保存場所（共有・正）
  - 基本: `<CentralModelFolder>\\_RevitMCP\\projects\\<docKey>\\chat\\writers\\*.jsonl`
    - `docKey` は **プロジェクト固有の安定ID**（ViewWorkspace/Ledger の ProjectToken と同等）で、同一フォルダ内の複数 `.rvt` のログ混入を防ぎます
  - 重要: 最初に `docPathHint`（中央モデルのパス推奨）と `docKey`（推奨）が必要。Revit Add-in が ViewActivated 時に自動設定します。

## 5) 参照ドキュメント（入口）
- エージェント向け統合ガイド: `Docs/Manuals/AGENT_README.md`
- クライアント開発ガイド（必読）: `Docs/Manuals/RevitMCP_Client_Dev_Guide.md`
- 接続ガイド（索引）: `Docs/Manuals/ConnectionGuide/INDEX.md`
- 接続クイックスタート: `Docs/Manuals/ConnectionGuide/QUICKSTART.md`
- コマンドの全体像（正規）: `GET /debug/capabilities`（または `docs/capabilities.jsonl`）/ `list_commands`（canonicalのみ）
- 人間向け索引: `Docs/Manuals/FullManual/README.md` / `Docs/Manuals/FullManual_ja/README.md`
- `Docs/Manuals/Commands/commands_index.json` は旧方式（ヒューリスティック）で、最新との差分が出ることがあります
- スクリプト一覧・使い方: `Scripts/Reference/README.md`
- プリマー（JSONL）: `Docs/Manuals/ConnectionGuide/PRIMER.jsonl`

## 6) 進捗・記録
- 主要な補助スクリプトは `Projects/<ProjectName>_<ProjectID>/Logs/*.json` に結果を保存します。
- リポジトリの履歴用: `Docs/Manuals/PROGRESS.jsonl` に自動でイベントを追記
- さらに細かい作業メモは、各プロジェクトの `Projects/<ProjectName>_<ProjectID>/README.md` に追記してください。

## 7) トラブルシュート（要点）
- ポートに到達できない: Revit を起動し MCP Add-in の待受を確認。`Test-NetConnection` が True であること。
- 書込みがタイムアウト: 対象要素を減らす/ビュー権限やテンプレートを確認/待ち時間を延長（スクリプトは 120s 目安）。
- `smoke_test` が利用不可: 読み取り中心に手順を進め、Add-in 更新後に安全版スクリプトで再試行。

---
この START_HERE.md を入口にすれば、Manuals 配下のガイド・スクリプト・コマンド索引に素早く到達し、Work 配下のプロジェクトフォルダで直ちに作業を開始できます。





