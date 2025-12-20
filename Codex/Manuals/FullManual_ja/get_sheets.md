# get_sheets

- カテゴリ: ViewOps
- 目的: このコマンドは『get_sheets』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_sheets

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| count | int | いいえ/状況による |  |
| includePlacedViews | bool | いいえ/状況による | false |
| nameContains | string | いいえ/状況による |  |
| namesOnly | bool | いいえ/状況による | false |
| sheetNumberContains | string | いいえ/状況による |  |
| skip | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_sheets",
  "params": {
    "count": 0,
    "includePlacedViews": false,
    "nameContains": "...",
    "namesOnly": false,
    "sheetNumberContains": "...",
    "skip": 0
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