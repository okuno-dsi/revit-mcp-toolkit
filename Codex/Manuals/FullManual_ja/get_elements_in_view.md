# get_elements_in_view

- カテゴリ: GeneralOps
- 目的: このコマンドは『get_elements_in_view』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_elements_in_view

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| categoryNameContains | string | いいえ/状況による |  |
| excludeImports | bool | いいえ/状況による | true |
| familyNameContains | string | いいえ/状況による |  |
| grouped | unknown | いいえ/状況による |  |
| includeElementTypes | bool | いいえ/状況による | false |
| includeIndependentTags | bool | いいえ/状況による | true |
| levelId | int | いいえ/状況による |  |
| levelName | string | いいえ/状況による |  |
| modelOnly | bool | いいえ/状況による | false |
| summaryOnly | bool | いいえ/状況による | false |
| typeNameContains | string | いいえ/状況による |  |
| viewId | unknown | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_elements_in_view",
  "params": {
    "categoryNameContains": "...",
    "excludeImports": false,
    "familyNameContains": "...",
    "grouped": "...",
    "includeElementTypes": false,
    "includeIndependentTags": false,
    "levelId": 0,
    "levelName": "...",
    "modelOnly": false,
    "summaryOnly": false,
    "typeNameContains": "...",
    "viewId": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- select_elements_by_filter_id
- select_elements
- 