# 柱芯線図ワークフロー 詳細説明版（将来拡張用）

## 1. 目的
本書は、柱芯線図作成フローを将来拡張しやすくするための詳細設計方針を定義する。  
特に、同一平面位置でレベルごとに柱種が切り替わるケース（RC/SRC/S）を前提に、
ビュー作成・寸法作成・シート構成の原則を明確化する。

対象:
- `Column_Coreline_Workflow_FullAuto_JA.md`
- `Column_Coreline_Workflow_PythonRunner_JA.md`
- `Scripts/PythonRunnerScripts/column_grid_coreline_full_auto.py`
- `Scripts/PythonRunnerScripts/column_grid_coreline_workflow_sample.py`

## 2. 基本原則
1. 柱芯線図の最小管理単位は「平面位置」ではなく「平面位置 x レベル」。
2. 寸法はレベルごとに再計算し、他レベルの値を流用しない。
3. 表示レンジ（View Range / Underlay）差異が結果へ影響するため、基準レベルを明示管理する。
4. シートは原則レベル別に作成し、必要時のみ複合シートへ集約する。
5. 検収可能性のため、出力JSONにレベル・柱種・寸法生成結果を残す。

## 3. レベル別柱種切替（RC/SRC/S）対応
### 3.1 想定パターン
- 低層: RC柱
- 中層: SRC柱
- 高層: S柱

### 3.2 何が変わるか
- 柱外形寸法（B x D）
- 芯ズレの有無（X/Y）
- タグ内容・優先タグタイプ
- 場合により基準となる通り芯表示長さ

### 3.3 必須対応
1. レベル単位で柱ビューを別作成する。
2. 柱外形寸法は当該ビュー（当該レベル）から再取得する。
3. 芯ズレ寸法はゼロ判定付きでレベルごとに個別作成する。
4. ビュー名・シート名へレベル識別子を必ず含める。

## 4. ビュー・シート構成ルール
## 4.1 ビュー命名
- 推奨: `CGA_<LEVEL>_<SOURCE>_COL_<ELEMENTID>`
- GRID_BASE: `CGA_<LEVEL>_<SOURCE>_GRID_BASE`

## 4.2 シート命名
- 推奨: `CGA_<LEVEL>_CORELINE`
- 複合シート運用時は接尾辞で区別:
  - `CGA_3FL_CORELINE_A`
  - `CGA_3FL_CORELINE_B`

## 4.3 縮尺
- 柱ビュー: 1/100
- GRID_BASE: 1/200
- 位置合わせアンカー: 同一グリッド交点（例: X1-Y3）

## 5. 寸法ルール（標準）
1. 柱外形寸法: 柱外形から 1000mm
2. 外形↔通り芯寸法: 柱外形から 600mm
3. 芯ズレ寸法:
   - X方向: 柱の上側（ゼロ時は省略）
   - Y方向: 柱の右側（ゼロ時は省略）
4. 位置丸め: 100mm単位
5. 2段寸法の段間: 350mm

## 6. タグルール（標準）
1. 構造柱タグは各柱ビューで 1 つのみ。
2. 位置: 柱外形右上から `X+100mm, Y+150mm`。
3. タグタイプは完全一致優先（例: `符号`）。

## 7. 失敗しやすい点と回避
1. 通り芯端点が柱から遠い
   - `adjust_grid_extents` ではなく `set_grid_segments_around_element_in_view` を使う。
2. テンプレート適用後に編集不能
   - 通り芯調整・寸法処理の後でテンプレート適用。
3. レベル混在で寸法が不整合
   - レベル別バッチ化し、1バッチごとに検収する。

## 8. 実行戦略（推奨）
1. 5本スモーク -> 30本本番の順で実行。
2. レベル混在案件は `TARGET_LEVEL_NAMES` 相当の分割実行。
3. 大量案件は `MAX_ALLOWED_COLUMNS` を利用して段階処理。

## 9. 出力JSON（推奨項目）
- `levelName`
- `columnProfile`（RC/SRC/S）
- `dimensionStatus`（ok / warn / skipped）
- `tagStatus`
- `gridAdjust.changed`
- `sheetName`

## 10. Runbook更新指針
要件追加時は以下順で更新する。
1. 本書（詳細版）に規約と判定ルールを追記
2. `FullAuto` に実行手順を追記
3. `PythonRunner` に運用手順を追記
4. サンプルスクリプト先頭コメント（`@feature`, `@keywords`）更新

---
最終更新: 2026-02-25
