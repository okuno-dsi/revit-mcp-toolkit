# 配筋プロトタイプ運用（フック＋回転の標準手順）

目的: Revit MCP で「配筋をすばやく試行・更新できる」プロトタイプ手順を標準化します。

前提:
- Revit 起動済み、Revit MCP サーバー稼働中（例: ポート `5210`）。
- Codex ワークスペースのルートでスクリプトを実行します。
- プロジェクトに `RebarBarType` / `RebarShape` が 0 件の場合、配筋コマンドは作成できません。
  - `list_rebar_bar_types` で件数確認 → 0 件なら `import_rebar_types_from_document` で構造テンプレート等から取り込み（best-effort）。

## 1) 選択ホストの「タグ付き自動配筋」を delete&recreate

構造柱/構造フレーム（梁）を選択して、以下を実行します:

```powershell
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command rebar_regenerate_delete_recreate --params "{""useSelectionIfEmpty"":true,""deleteMode"":""tagged_only"",""tag"":""RevitMcp:AutoRebar"",""options"":{""tagComments"":""RevitMcp:AutoRebar""}}"
```

注意:
- delete を含む write コマンドです。ただし削除対象は「ホスト内」かつ「Comments に tag を含む」鉄筋に限定されます。
- ホストごとにトランザクションを分離し、失敗したホストはそのホスト分だけロールバックされます。

## 2) 鉄筋のフック設定（始端/終端フック＋回転）を一括適用

### A（推奨）: 参照鉄筋からコピー

1. UI で調整済みの「正しい」鉄筋を **先頭に** 選択する（参照）。
2. 続けて、適用したい鉄筋も選択する。
3. 実行:

```powershell
python Manuals/Scripts/rebar_set_hooks_and_rotations_on_selection.py --port 5210
```

このスクリプトは、参照のフック設定を以下4つのインスタンスパラメータとして読み取り、残りの選択要素へコピーします:
- `始端のフック`
- `終端のフック`
- `始端でのフックの回転`
- `終端でのフックの回転`

### B: フック種別IDと回転を指定して強制設定

`RebarHookType` の elementId を既知の場合:

```powershell
python Manuals/Scripts/rebar_set_hooks_and_rotations_on_selection.py --port 5210 --hook-type-id 4857530 --start-rot-deg 0 --end-rot-deg 180
```

重要:
- Revit の角度パラメータは、値によって `180°` が `0°` に正規化される挙動があるため、  
  このスクリプトは `180°` を要求された場合に `179.999°` を設定する回避策を既定で有効にしています。

## 3) 検証とログ

スクリプトは自動的に検証し、結果を次に保存します:
- `Work/RevitMcp/<port>/rebar_hooks_apply_*.json`

## 次: 柱/梁パラメータと配筋方法の突合せへ

「柱や梁のパラメータ ↔ 配筋ロジック」を照合する前に、まず次を整理するのが効率的です:
- どの属性を Type パラメータ／Instance パラメータから取るか（`RebarMapping.json`）
- どの項目を `rebar_plan_auto.options` で上書きするか（優先順位）

その上で、
- `rebar_mapping_resolve`（マッピング解決結果）
- `rebar_plan_auto`（計画）
- 実際の鉄筋＋パラメータ＋レイアウト
を突合せして、「属性情報どおりに配筋されたか」を機械的に検証できるようにします。

### SIRBIM（現行プロジェクト）の注意
現行プロジェクトは **SIRBIM** 系ファミリを前提に「タイプパラメータから配筋属性を読み取る」運用をします。  
ただしファミリによってパラメータ名/型は変わるため、`RebarMapping.json` のプロファイルで吸収し、実行前に Snoop / `rebar_mapping_resolve` で確認してください。

- 参照: `Manuals/Runbooks/SIRBIM/Rebar_Params_StructuralColumns_JA.md`
- 参照: `Manuals/Runbooks/SIRBIM/Rebar_Params_StructuralFrames_JA.md`

### まずはマッピング解決結果をダンプ（選択ホスト）
ホスト要素を選択して以下を実行します:

```powershell
python Manuals/Scripts/rebar_mapping_dump_selected_hosts.py --port 5210 --include-debug
```
