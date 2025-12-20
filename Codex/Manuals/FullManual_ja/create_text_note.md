# create_text_note

- カテゴリ: AnnotationOps
- 目的: このコマンドは『create_text_note』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_text_note

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| batchSize | int | いいえ/状況による | 50 |
| maxMillisPerTx | int | いいえ/状況による | 100 |
| refreshView | bool | いいえ/状況による | false |
| startIndex | int | いいえ/状況による | 0 |
| text | string | いいえ/状況による |  |
| typeName | string | いいえ/状況による |  |
| unit | string | いいえ/状況による |  |
| viewId | int | いいえ/状況による |  |
| x | number | いいえ/状況による | 0.0 |
| y | number | いいえ/状況による | 0.0 |
| z | number | いいえ/状況による | 0.0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_text_note",
  "params": {
    "batchSize": 0,
    "maxMillisPerTx": 0,
    "refreshView": false,
    "startIndex": 0,
    "text": "...",
    "typeName": "...",
    "unit": "...",
    "viewId": 0,
    "x": 0.0,
    "y": 0.0,
    "z": 0.0
  }
}
```

## 関連コマンド
## 関連コマンド
- get_detail_lines_in_view
- create_detail_line
- create_detail_arc
- move_detail_line
- rotate_detail_line
- delete_detail_line
- get_line_styles
- 