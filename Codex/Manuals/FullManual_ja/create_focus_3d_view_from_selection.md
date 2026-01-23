# 選択要素から 3D クリッピングビュー（セクションボックス）を作成

- カテゴリ: ViewOps
- 種別: write
- 目的: 選択要素を包む SectionBox を設定した 3D ビューを作成し、必要に応じてアクティブ化します。

現在の選択要素（または指定した `elementIds`）を包む **SectionBox** を設定した新しい 3D ビュー（正投影）を作成し、必要に応じてそのビューをアクティブにします。

このコマンドは、複数コマンドを連続実行する方式（`get_selected_element_ids` → `create_3d_view` → `activate_view` → `set_section_box_by_elements` → `view_fit`）よりも **1回のRPCで完結** させることで、往復回数を減らし待ち時間を短縮する目的で追加されています。

## コマンド
- 正式: `view.create_focus_3d_view_from_selection`
- 別名: `create_focus_3d_view_from_selection`, `create_clipping_3d_view_from_selection`

## パラメータ
- `elementIds`（任意, `int[]`）: 対象要素ID。省略時は現在選択を使用。
- `paddingMm`（任意, `double`, 既定 `200`）: 余白（mm）。
- `name`（任意, `string`）: ビュー名を明示指定。
- `viewNamePrefix`（任意, `string`, 既定 `Clip3D_Selected_`）: `name` 未指定時の接頭辞。
- `activate`（任意, `bool`, 既定 `true`）: 作成したビューをアクティブ化。
- `templateViewId`（任意, `int`）: ビューテンプレートを適用（表示/グラフィックに影響する可能性があります）。

## 例（JSON-RPC）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "view.create_focus_3d_view_from_selection",
  "params": {
    "paddingMm": 200,
    "viewNamePrefix": "Clip3D_Selected_",
    "activate": true
  }
}
```

## 戻り値（概形）
- `viewId`, `name`, `activated`
- `sectionBox`: `{min:{x,y,z}, max:{x,y,z}}`（**mm**）
- `skipped`: BoundingBox 集計から除外された要素（BoundingBox が無い等）
