# rotate_detail_line

- カテゴリ: AnnotationOps
- 目的: このコマンドは『rotate_detail_line』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: rotate_detail_line

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| angleDeg | number | いいえ/状況による |  |
| elementId | int | いいえ/状況による |  |
| origin | object | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "rotate_detail_line",
  "params": {
    "angleDeg": 0.0,
    "elementId": 0,
    "origin": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- get_detail_lines_in_view
- create_detail_line
- create_detail_arc
- move_detail_line
- delete_detail_line
- get_line_styles
- set_detail_line_style
- 