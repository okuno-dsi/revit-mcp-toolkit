# get_element_info

- カテゴリ: Misc
- 目的: このコマンドは『get_element_info』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_element_info

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| elementId | unknown | いいえ/状況による |  |
| elementIds | unknown | いいえ/状況による |  |
| includeVisual | bool | いいえ/状況による | false |
| rich | bool | いいえ/状況による | false |
| uniqueId | unknown | いいえ/状況による |  |
| uniqueIds | unknown | いいえ/状況による |  |
| viewId | int | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_element_info",
  "params": {
    "elementId": "...",
    "elementIds": "...",
    "includeVisual": false,
    "rich": false,
    "uniqueId": "...",
    "uniqueIds": "...",
    "viewId": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- stash_selection
- restore_selection
- 