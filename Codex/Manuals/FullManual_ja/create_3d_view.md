# create_3d_view

- カテゴリ: ViewOps
- 目的: このコマンドは `create_3d_view` ビューを新規作成します。

## 概要
このコマンドは JSON-RPC で Revit MCP アドインに送信され、目的に記載した処理を行います。下記のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: `create_3d_view`

### パラメータ
| 名前            | 型     | 必須              | 既定値 |
|-----------------|--------|-------------------|--------|
| name            | string | いいえ / 推奨     |        |
| templateViewId  | int    | いいえ / 推奨     | 0      |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_3d_view",
  "params": {
    "name": "MCP_3D",
    "templateViewId": 0
  }
}
```

## 運用上の注意（3Dビューは「コピー」ではなく新規作成）

- MCP で要素を色分けしたり、位置を変更したりする 3D 作業ビューは、Revit UI の「ビューを複製」ではなく、この `create_3d_view` コマンドで**新しく Isometric 3D ビューを作成**して使うことを推奨します。
- すでにテンプレートが適用された 3D ビューをコピーして使うと、
  - テンプレートによる可視性制御の影響で、カテゴリを隠せない・色が反映されない
  - MCP 側の自動生成ビュー（Working 3D など）と混在し、どのビューに処理したか分かりにくくなる  
  といった問題が起きやすくなります。
- 3D での MCP 作業は、基本ルールとして:
  1. `create_3d_view` で専用の 3D ビューを作る（例: `MCP_SB440_3D` など）。
  2. そのビューに対して `set_visual_override` や `set_section_box_by_elements` などの可視化・抽出コマンドを適用する。  
  という流れで運用してください。

## 関連コマンド
- get_view_info
- save_view_state
- restore_view_state
- create_view_plan
- create_section
- create_elevation_view
- compare_view_states

