# explode_model_line_to_detail

- カテゴリ: AnnotationOps
- 目的: このコマンドは『explode_model_line_to_detail』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: explode_model_line_to_detail

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| viewId | int | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "explode_model_line_to_detail",
  "params": {
    "viewId": 0
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