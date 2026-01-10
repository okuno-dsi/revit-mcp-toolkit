# ChangeLog 2026-01-07 (AutoRebar / RUG)

## 目的
- RUGファミリ（例: 構造柱 `RC_C_B : 1C1`）で、`D22` 等の **鉄筋タイプ名がプロジェクトに存在しない**場合でも配筋できるようにする。
- ファミリ固有パラメータは **ソースコードにハードコードせず**、`RebarMapping.json` のプロファイルで吸収する。

## 変更点（要約）
- `RebarBarType` の解決を強化:
  - 既定は従来どおり「名前の完全一致」。
  - 見つからない場合、指定文字列に `D25` 等の径が含まれていれば **径一致で `RebarBarType` を探索**して解決（命名規則に依存しない）。
  - 応答に `mainBarTypeResolve` / `tieBarTypeResolve` を付与して、requested/resolved/resolvedBy/diameterMm を返す。
- `RebarMapping.json` に RUG 柱用プロファイルを追加:
  - `rug_column_attr_v1`（priority 110）
  - 主筋: `D_main`、帯筋: `D_hoop`、ピッチ: `pitch_hoop_panel` 等を吸収（best-effort）。
- `RebarMapping.json` に RUG 梁（構造フレーム）用プロファイルを追加:
  - `rug_beam_attr_v1`（priority 120）
  - 主筋本数: `N_main_top_total` / `N_main_bottom_total`、スターラップピッチ: `pitch_stirrup*` を吸収（best-effort）。
  - 2段/3段筋: `N_main_*_1st/_2nd/_3rd` を吸収し、断面内で層をスタックして作成（簡易近似、`options.beamMainBarLayerClearMm`）。
- mappingの単位解釈を改善:
  - `reinforcementSpacing` 等の length-like spec を mm に変換して読み取るように修正（`Common.Arrangement.Spacing` などが ft 値のままになる問題を回避）。
- 鉄筋中心間離隔テーブル（ファイル）を追加:
  - `RebarBarClearanceTable.json` を追加し、D10〜D41 の「中心-中心」離隔（mm）を定義。
  - 梁の2段/3段筋の層ピッチは、このテーブルを優先して使用（無い径のみフォールバック）。
  - `rebar_plan_auto` の応答に `beamMainBarClearanceCheck` を追加し、計画上の最小間隔チェック結果を返す（作成前の検証）。
- プロジェクトに `RebarBarType` / `RebarShape` が 0 件の場合の救済コマンドを追加:
  - `list_rebar_bar_types`（read）
  - `import_rebar_types_from_document`（write）
- 帯筋/スターラップのフック解決を改善:
  - `RebarStyle.StirrupTie` に対して `RebarHookType` を `HookStyle`（可能なら）/名称ヒューリスティックで優先・選別し、`標準フック - 135 度` のような非互換指定でも角度ベースで適合フックへフォールバック（見つからなければフック無効化、best-effort）。
  - せん断補強筋（帯筋/スターラップ）は「閉ループ形状」のまま作成し、作成後に `始端/終端のフック` と `終端でのフックの回転(既定179.9°)` を段階的に設定（SubTransaction + Regenerate、best-effort）。

## 影響するコマンド
- `rebar_plan_auto`
- `rebar_apply_plan`
- `rebar_regenerate_delete_recreate`
- `rebar_mapping_resolve`（新プロファイルの適用確認に使用）
- `list_rebar_bar_types`
- `import_rebar_types_from_document`

## 変更ファイル
- `RevitMCPAddin/Core/RebarAutoModelService.cs`
- `RevitMCPAddin/Core/RebarMappingService.cs`
- `RevitMCPAddin/RebarMapping.json`
- `RevitMCPAddin/RebarBarClearanceTable.json`
- `RevitMCPAddin/Core/Rebar/RebarBarClearanceTableService.cs`
- `RevitMCPAddin/Commands/Rebar/ListRebarBarTypesCommand.cs`
- `RevitMCPAddin/Commands/Rebar/ImportRebarTypesFromDocumentCommand.cs`
- `RevitMCPAddin/RevitMcpWorker.cs`
- `RevitMCPAddin/RevitMCPAddin.csproj`
- `Manuals/UpdateLog.md`
- `Manuals/FullManual/rebar_plan_auto.md`
- `Manuals/FullManual_ja/rebar_plan_auto.md`
- `Manuals/Runbooks/RUG/Rebar_Params_RUG_SelectedTypes_JA.md`
- `Manuals/FullManual/list_rebar_bar_types.md`
- `Manuals/FullManual_ja/list_rebar_bar_types.md`
- `Manuals/FullManual/import_rebar_types_from_document.md`
- `Manuals/FullManual_ja/import_rebar_types_from_document.md`

## テスト観点（最小）
- RUG柱（例: `elementId=110326`）を選択して `rebar_plan_auto` → `rebar_regenerate_delete_recreate` を実行し、`BAR_TYPE_NOT_FOUND` が出ないこと。
- 応答に `mainBarTypeResolve` / `tieBarTypeResolve` が出ること（径フォールバック時のみ）。
- `list_rebar_bar_types` が `count>0` であること（0件なら `import_rebar_types_from_document` で取り込みが必要）。
