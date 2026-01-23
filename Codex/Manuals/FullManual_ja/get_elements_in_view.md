# get_elements_in_view

- カテゴリ: GeneralOps
- 目的: このコマンドは『get_elements_in_view』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

補足:
- `viewId` が省略された場合は **アクティブビュー**が使われ、`usedActiveView=true` が返ります。
- `categoryNames` は BuiltInCategory 名を解決できる場合は自動で解決され、解決できない場合は表示名で一致検索します。
- `count` / `skip` はトップレベルのページング別名として使えます（`_shape.page.limit/skip` と同様）。
- 応答には `rows` に加えて `items` がエイリアスとして返ります。

## 使い方
- メソッド: get_elements_in_view

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| categoryNameContains | string | いいえ/状況による |  |
| categoryNames | string[] | いいえ/状況による |  |
| categoryIds | int[] | いいえ/状況による |  |
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
| elementIds | int[] | いいえ/状況による |  |
| count | int | いいえ/状況による |  |
| skip | int | いいえ/状況による |  |
| viewId | unknown | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_elements_in_view",
  "params": {
    "categoryNameContains": "...",
    "categoryNames": ["Walls", "Floors"],
    "categoryIds": [ -2000011 ],
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
    "elementIds": [ 123, 456 ],
    "count": 100,
    "skip": 0,
    "viewId": "..."
  }
}
```

## 関連コマンド
- select_elements_by_filter_id
- select_elements
