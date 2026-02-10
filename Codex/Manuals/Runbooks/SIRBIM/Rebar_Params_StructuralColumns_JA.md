# SIRBIM（現行プロジェクト）: 構造柱タイプパラメータと配筋の対応メモ

このプロジェクトでは、構造柱ファミリが **SIRBIM** 系であり、配筋の属性（主筋本数・帯筋ピッチ等）を **タイプパラメータ** で保持しています。  
以後の配筋コマンド（`rebar_plan_auto` / `rebar_apply_plan` / `rebar_regenerate_delete_recreate`）は、基本方針として **これらのタイプパラメータを参照して配筋** する運用を前提にします。

重要:
- ファミリ作成者や言語によってパラメータ名は変わります。**他の設計者/ファミリでは同じ名前で存在しない可能性**があります。
- そのため実装は「パラメータ名をソースにベタ書き」ではなく、`RebarMapping.json` の **プロファイル（変換テーブル）**で吸収し、`rebar_mapping_resolve` で検証しながら運用してください。
- パラメータが不明確な場合は、実行前に **Snoop（Snoop DB / Snoop Current Selection）** または `get_type_parameters_bulk` / `get_element_info (rich=true)` で存在・型・単位を確認し、ユーザーへ確認します。

## 参照CSV（注釈入り）
ユーザーが CSV の J 列（note/説明）に「配筋のための説明」を追記した参照ファイル:
- `Manuals/Runbooks/SIRBIM/structural_column_type_parameters_with_values_SIRBIM_annotated.csv`

元データ（自動出力）:
- `Scripts/Reference/dump_structural_column_type_param_definitions.py`

### 注釈（note）を維持したまま再出力する
```powershell
python Scripts/Reference/dump_structural_column_type_param_definitions.py --port 5210 --include-values --annotation-csv Manuals/Runbooks/SIRBIM/structural_column_type_parameters_with_values_SIRBIM_annotated.csv
```

## J列（note）に記載された主要項目（抜粋）
以下は、上記 CSV の note 列に記載された「配筋向け説明」のうち、パラメータ名をキーに整理したものです（SIRBIM前提）。

| paramName | 説明（note） |
|---|---|
| B | Y軸方向の幅 |
| D | X軸方向の幅 |
| 柱脚フープX本数 | 柱脚のX軸方向に並ぶフープ筋の本数 |
| 柱脚フープY本数 | 柱脚のY軸方向に並ぶフープ筋の本数 |
| 柱脚フープピッチ | 柱脚のフープのピッチ |
| 柱脚フープ径 | 柱脚のフープ筋の径 |
| 柱脚主筋X1段筋太径本数 | 柱脚のY軸方向の幅の辺に沿って並ぶ主筋の本数 |
| 柱脚主筋Y1段筋太径本数 | 柱脚のX軸方向の幅の辺に沿って並ぶ主筋の本数 |
| 柱脚主筋太径 | 柱脚の主筋の径 |
| 柱頭フープX本数 | 柱頭のX軸方向に並ぶフープ筋の本数 |
| 柱頭フープY本数 | 柱頭のY軸方向に並ぶフープ筋の本数 |
| 柱頭フープピッチ | 柱頭のフープのピッチ |
| 柱頭フープ径 | 柱頭のフープ筋の径 |
| 柱頭主筋X1段筋太径本数 | 柱頭のY軸方向の幅の辺に沿って並ぶ主筋の本数 |
| 柱頭主筋Y1段筋太径本数 | 柱頭のX軸方向の幅の辺に沿って並ぶ主筋の本数 |
| 柱頭主筋太径 | 柱頭の主筋の径 |

## 運用上の注意（必須）
- **この表は「現行プロジェクト（SIRBIM）」の前提**です。他のファミリでは:
  - パラメータ名が無い/違う
  - 型（整数/実数/文字列）が違う
  - 単位（mm/内部単位など）が違う
  - そもそも type ではなく instance にある
  などが起こりえます。
- 実際に配筋で使う前に、対象ホスト（柱/梁）を選択して次を実行し、**どのキーがどの値に解決されるか**を確認してください:
  - `python Scripts/Reference/rebar_mapping_dump_selected_hosts.py --port 5210 --include-debug`




