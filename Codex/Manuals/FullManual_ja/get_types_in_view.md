# get_types_in_view

- カテゴリ: GeneralOps
- 目的: このコマンドは『get_types_in_view』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_types_in_view

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| includeCounts | bool | いいえ/状況による | true |
| includeElementTypes | bool | いいえ/状況による | false |
| includeIndependentTags | bool | いいえ/状況による | false |
| includeTypeInfo | bool | いいえ/状況による | true |
| modelOnly | bool | いいえ/状況による | true |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_types_in_view",
  "params": {
    "includeCounts": false,
    "includeElementTypes": false,
    "includeIndependentTags": false,
    "includeTypeInfo": false,
    "modelOnly": false
  }
}
```

## 関連コマンド
## 関連コマンド
- select_elements_by_filter_id
- select_elements
- 