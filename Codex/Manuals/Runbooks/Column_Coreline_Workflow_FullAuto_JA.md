# 柱芯線図フル自動 RunBook（元柱ビュー不要）

> 詳細説明版: `Column_Coreline_Workflow_Detailed_JA.md`

## 目的
- 既存の「柱別ビュー」が無くても、以下を一括実行する。
1. 柱別ビュー作成
   - ビュー名に元レベル名を含める（例: `CGA_3FL_0216xxxx_COL_123456`）
   - 柱ビューのビュータイプを `柱芯線図` に設定
2. 通り芯調整（柱中心まわり）
3. 通り芯記号（バブル）非表示
4. 柱↔通り芯寸法の自動展開
5. 柱ビュー右上への構造柱タグ配置
6. シート重ね配置（柱 1/100 + 通り芯 1/200）

## レベル別柱種切替（RC/SRC/S）への対応方針
- 前提: 同一平面位置でも、レベルごとに柱種・外形寸法が変わることがある。
  - 例: 下層 `RC柱` → 中層 `SRC柱` → 上層 `鉄骨柱`
- 運用ルール:
1. 柱ビューは「平面位置」単位ではなく「平面位置 x レベル」単位で作成する。
2. 寸法は必ず当該レベルの柱外形から再計算する（他レベル流用はしない）。
3. シートは原則「レベル別」に分ける。混在させる場合も、ビュー名でレベル識別を必須化する。
4. 通り芯との芯ズレ寸法（X/Y）は、各レベルで独立に判定する。
5. 将来の拡張では、柱種プロファイル（RC/SRC/S）を出力JSONへ保持して検収できる形を推奨する。
- 命名推奨:
  - ビュー: `CGA_<LEVEL>_<PREFIX>_COL_<ELEMENTID>`
  - シート: `CGA_<LEVEL>_CORELINE`

## 対象スクリプト
- `Scripts/PythonRunnerScripts/column_grid_coreline_full_auto.py`

## 入力起点（どちらでも可）
- 起点A: 「このビューから」  
  - `SOURCE_VIEW_NAME=""` のまま、対象ビューをアクティブにして実行。
- 起点B: 「このレベル伏図から」  
  - `SOURCE_LEVEL_NAME="3FL"` のように指定して実行。

## 寸法ルール（本スクリプトの既定）
- `X方向ズレ寸法` は **柱の上側** に配置。
- `Y方向ズレ寸法` は、選択寸法（Y）があればその側を優先。なければ既存寸法の多数決。
- 寸法線位置オフセットは **100mm単位** に丸め（`OFFSET_ROUND_MM=100`）。
- 最小オフセットは `100mm`（`OFFSET_MIN_MM=100`）。
- sourceテンプレート寸法が無い場合でも、既定値で寸法生成を継続する。
  - 柱外形↔通り芯: 600mm
  - 柱外形寸法: 1000mm（600 + tierGap 400）
  - 芯ズレ寸法: 600mm（上/右）

## 実務運用の仕上げ（推奨）
- 生成後、`*_GRID_BASE` ビューは通り芯記号（グリッドバブル）を表示する。
- 各 `*_COL_*` ビューは「トリミング領域を表示」を OFF にする。
- 各 `*_COL_*` ビューは、ビュータイプ `柱芯線図` になっていることを確認する。
- 各 `*_COL_*` ビューで、構造柱タグを右上に 1 つ配置する（既存タグは置換）。
  - 位置: 柱外形右上から `X+100mm, Y+150mm`（既定）
- 各 `*_COL_*` ビューへ、以下 4 本の寸法を必ず展開する。
1. 柱外形↔通り芯（X方向）
2. 柱外形↔通り芯（Y方向）
3. 芯ズレ寸法（X）
4. 芯ズレ寸法（Y）

## 通り芯の表示範囲（端点）に関する重要注意
- `element.adjust_grid_extents` はモデル全体の外形BBox基準で端点を作るため、  
  `*_COL_*` 柱単体ビューでは端点が柱から遠くなることがある。
- 柱単体ビューの端点補修は `element.set_grid_segments_around_element_in_view` を使う。
  - 例: `{"viewId": <COLビューID>, "elementId": <柱ID>, "halfLengthMm": 1000, "detachViewTemplate": true}`

## 通り芯端点補修（COLビュー）
1. `*_COL_*` ビューをアクティブにする。
2. `element.set_grid_segments_around_element_in_view` を実行する。
3. 実行結果を確認する。
   - `changed >= 1`: 補修成功。
   - `errors` に `curve ... not coincident ... datum plane` が出る場合は「既知制約」扱い。

## 既知制約（curve not coincident）
- 一部の通り芯は Revit API 制約で `SetCurveInView` が失敗し、端点を自動短縮できない場合がある。
- 運用手順:
1. まず `gridIds` 無指定で実行し、短縮可能な通り芯を先に反映する。
2. 失敗した通り芯のみ手動で端点を調整する。
3. `changed` / `errors` を記録して検収する。

## 寸法展開コマンドの注意（重要）
- `view.apply_column_grid_dimension_standard_to_views` は、`sourceViewName` と `targetViewNameRegex` が同じ正規表現で柱IDを抽出できる必要がある。
- 例:
1. source を一時的に `TMP_COL_5746085` のようにリネーム
2. `targetViewNameRegex` を `^(?:CGA_0219130754_COL_|TMP_COL_)(\\d+)$` のように指定
3. 適用後に source 名を元へ戻す

## 実行前の推奨
1. 起点ビューに、対象柱と通り芯が表示されていることを確認。
2. 可能なら、Y方向ズレ寸法を1本選択しておく。
   - これによりY側配置の判断が安定する。
3. テンプレート名を使う場合:
   - `GRID_TEMPLATE_NAME`
   - `COLUMN_TEMPLATE_NAME`

## 実行手順
1. Python Runner で `column_grid_coreline_full_auto.py` を開く。
2. 必要なら先頭パラメータを調整:
   - `SOURCE_VIEW_NAME` / `SOURCE_LEVEL_NAME`
   - `COLUMN_VIEW_TYPE_NAME`（既定: `柱芯線図`）
   - `GRID_BASE_USE_COLUMN_VIEW_TYPE`（既定: `True`。`GRID_BASE` も `COLUMN_VIEW_TYPE_NAME` に揃える）
   - `COLUMN_MARGIN_MM`, `GRID_HALF_LENGTH_MM`
   - `FORCE_AXIS_X_SIDE`, `OFFSET_ROUND_MM`
   - `PLACE_STRUCTURAL_COLUMN_TAG`, `TAG_OFFSET_RIGHT_MM`, `TAG_OFFSET_UP_MM`
3. `Run` 実行。
4. JSON出力で以下を確認:
   - `sourceColumnId`
   - `dimensionTemplateAdjust.ok`
   - `dimensionApply.ok`
   - `structuralColumnTagType.ok`
   - `sheet.ok`
5. 仕上げ処理を実施:
   - `*_GRID_BASE`: 通り芯記号 ON
   - `*_COL_*`: トリミング領域表示 OFF
   - `*_COL_*`: 右上タグ 1つ（重複なし）
   - 寸法 4 本（外形X/Y + 芯ズレX/Y）が各ビューにあることを確認

## 出力結果の見方
- `items[]`: 柱別ビュー作成結果（各 `viewId`, `anchorGridA/B` など）。
- `items[].columnTag`: 柱タグ配置結果（`ok`, `tagId`, `locationMm`, `deletedExistingTags`）。
- `dimensionTemplateAdjust`: sourceテンプレート寸法の補正結果。
  - `targetAxisXSide="top"` なら X方向は上側化済み。
  - `items[].deltaMm` で丸め移動量を確認可能。
- `dimensionApply`: 寸法展開コマンド結果。
- `sheet`: シート重ね配置結果。

## 失敗時の対処
- `source template view has no dimensions`
  - 起点ビューに寸法が無い可能性。Y方向寸法を1本作成/選択して再実行。
- `column id を解決できません`
  - 起点ビューに構造柱が表示されていない、または選択情報が無い。
- `dimensionApply` 失敗
  - 対象ビュー名正規表現不一致や、sourceテンプレート寸法不足を確認。

## 運用メモ
- このRunBookは、元の柱ビュー（`*_COL_*`）が無い状態を前提に設計。
- 柱基準点が通り芯とズレる場合も、柱↔通り芯寸法（3参照寸法）を展開して反映。

## 安定化修正（2026-02-24）
以下は実運用で発生した停止・不安定化を回避するための確定事項。

1. 寸法展開コマンドは、内部で `ActiveView` を切り替えない。
   - 既知のクラッシュ要因（ビュー切替中のUI状態不整合）を排除。
2. ビューテンプレート検出は `view.get_views(includeTemplates=true)` を使用する。
   - テンプレートが存在していても未検出になる誤判定を防止。
3. 柱ビューのテンプレート適用は、通り芯調整・寸法処理の後に実施する。
   - 先適用でテンプレートロックにより編集不可になる事象を回避。
4. `hide_elements_in_view` は、非表示対象IDが空のとき呼び出さない。
   - 空配列呼び出し時のエラー/無駄な失敗ログを防止。
5. Python Runner用スクリプトは `requests` 未導入環境でも動くよう、
   `urllib` フォールバックを持つ。

## 推奨実行パターン（安全）
1. まず 5 本でスモークテストを実施（同じ設定・同じビュー起点）。
2. 問題なければ 30 本の本番実行へ拡張。
3. 各実行後に JSON 出力で以下のみ確認する。
   - `ok=true`
   - `dimensionApply.okCount` が対象本数と一致
   - `sheet.ok=true`

## 将来拡張メモ（本RunBookの適用範囲）
- 本RunBookは現状の全自動フローを扱う。レベル跨ぎの柱種切替がある案件では、以下を追加要件として扱う。
1. レベル別バッチ実行（1レベルずつ生成・検収）
2. 柱種（RC/SRC/S）検出ルールの固定化
3. シート束（レベルごと）の自動編成
- 具体的な仕様検討は `Column_Coreline_Workflow_Detailed_JA.md` を基準に進める。

## 実測検証（2026-02-24）
- 5本検証: 正常完了、クラッシュなし、シート配置と寸法反映を確認。
- 30本検証: 正常完了、クラッシュなし、柱ビューと重ね配置を確認。
