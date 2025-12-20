# set_section_box_by_elements

- カテゴリ: ViewOps
- 目的: このコマンドは『set_section_box_by_elements』を設定します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: set_section_box_by_elements

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| elementIds | unknown | いいえ/状況による |  |
| paddingMm | number | いいえ/状況による | 0.0 |
| viewId | unknown | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_section_box_by_elements",
  "params": {
    "elementIds": "...",
    "paddingMm": 0.0,
    "viewId": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- get_view_info
- save_view_state
- restore_view_state
- create_view_plan
- create_section
- create_elevation_view
- compare_view_states
- 