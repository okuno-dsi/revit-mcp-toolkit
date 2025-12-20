# get_tag_symbols

- カテゴリ: AnnotationOps
- 目的: このコマンドは『get_tag_symbols』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_tag_symbols

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| category | string | いいえ/状況による |  |
| count | int | いいえ/状況による |  |
| familyName | string | いいえ/状況による |  |
| nameContains | string | いいえ/状況による |  |
| namesOnly | bool | いいえ/状況による | false |
| skip | int | いいえ/状況による | 0 |
| summaryOnly | bool | いいえ/状況による | false |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_tag_symbols",
  "params": {
    "category": "...",
    "count": 0,
    "familyName": "...",
    "nameContains": "...",
    "namesOnly": false,
    "skip": 0,
    "summaryOnly": false
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