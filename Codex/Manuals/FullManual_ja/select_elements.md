# select_elements

- カテゴリ: GeneralOps
- 目的: このコマンドは『select_elements』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: select_elements

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| elementId | unknown | いいえ/状況による |  |
| elementIds | unknown | いいえ/状況による |  |
| zoomTo | bool | いいえ/状況による | false |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "select_elements",
  "params": {
    "elementId": "...",
    "elementIds": "...",
    "zoomTo": false
  }
}
```

## 関連コマンド
## 関連コマンド
- get_types_in_view
- select_elements_by_filter_id
- 