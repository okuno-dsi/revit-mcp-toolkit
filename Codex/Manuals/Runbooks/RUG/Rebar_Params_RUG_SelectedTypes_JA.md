# RUGファミリ（現行プロジェクト）: 指定タイプのタイプパラメータ出力（柱/梁に分割）

対象（ユーザー指定）:
- 構造柱: `RC_C_B : 1C1`
- 構造フレーム: `RC_B : FG13`

これらは本プロジェクト内で「RUGファミリ」と呼ばれているものです。

## 参照CSV（注釈入り・Manuals側）
ユーザーが追記する列（Excel上での M 列など）を `note` として正規化し、柱/梁で分割して保持します。

- 構造柱（RUG）: `Manuals/Runbooks/RUG/rug_type_parameters_with_values_20260107_091425_structural_column.csv`
- 構造フレーム（RUG）: `Manuals/Runbooks/RUG/rug_type_parameters_with_values_20260107_091425_structural_frame.csv`

## 再出力（更新）方法
1) 元データを出力（RUG）
```powershell
python Scripts/Reference/dump_rug_selected_type_parameters.py --port 5210 --column-family RC_C_B --column-type 1C1 --frame-family RC_B --frame-type FG13
```

2) Excel等で追記したCSVを、`kind` 列で分割しつつ `note` に正規化
```powershell
python Scripts/Reference/normalize_split_type_params_csv.py --src Projects/RevitMcp/5210/rug_type_parameters_with_values_<timestamp>.csv --split-key kind --out-dir Projects/RevitMcp/5210 --base-name rug_type_parameters_with_values_<timestamp> --copy-to Manuals/Runbooks/RUG
```

## 注意（必須）
- 他の設計者が作成したファミリでは、同じパラメータ名/型で存在しない可能性があります。
- 実装は「パラメータ名のハードコード」ではなく、`RebarMapping.json` のプロファイル（変換テーブル）で吸収してください。
- 不明な場合は、Snoop または `get_type_parameters_bulk` / `get_element_info (rich=true)` で事前確認してから進めます。

## RebarMapping.json 側の対応（RUG）
`RebarMapping.json` に `rug_column_attr_v1` を追加し、RUG 柱タイプの主要キーを次のように吸収します（best-effort）。
- `Common.MainBar.BarType` ← `D_main`（例: `D25`）
- `Common.TieBar.BarType` ← `D_hoop`（例: `D13`）
- `Common.Arrangement.Spacing` ← `pitch_hoop_panel`（例: 150mm）
- `Column.Attr.MainBar.BarsPerFace` ← `N_main_X_1st_bottom` 等

あわせて、RUG 梁（構造フレーム）向けに `rug_beam_attr_v1` を追加し、梁タイプの主要キーを次のように吸収します（best-effort）。
- `Common.MainBar.BarType` ← `D_main`（例: `D25`）
- `Common.TieBar.BarType` ← `D_stirrup`（例: `D13`）
- `Beam.Attr.MainBar.TopCount/TopCount2/TopCount3` ← `N_main_top_1st/_2nd/_3rd`（例: 4+2+0）
- `Beam.Attr.MainBar.BottomCount/BottomCount2/BottomCount3` ← `N_main_bottom_1st/_2nd/_3rd`（例: 4+2+0）
- `Beam.Attr.Stirrup.Pitch*Mm` ← `pitch_stirrup` / `pitch_stirrup_start` / `pitch_stirrup_end`（reinforcementSpacing は mm に正規化）

補足:
- 現状の配筋ロジックは「梁断面内に 1〜3層まで主筋をスタックする」簡易近似です。
  - 層ピッチ（中心-中心）は `RebarBarClearanceTable.json`（中心間離隔テーブル）を優先し、無い径のみ `barDiameter + options.beamMainBarLayerClearMm` にフォールバックします。




