# update_text_note_parameter

- カテゴリ: AnnotationOps
- 目的: このコマンドは『update_text_note_parameter』を更新します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: update_text_note_parameter

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| applyToType | bool | いいえ/状況による | false |
| elementId | int | いいえ/状況による | 0 |
| paramName | string | いいえ/状況による |  |
| unit | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_text_note_parameter",
  "params": {
    "applyToType": false,
    "elementId": 0,
    "paramName": "...",
    "unit": "..."
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