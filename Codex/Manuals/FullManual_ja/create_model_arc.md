# create_model_arc

- カテゴリ: AnnotationOps
- 目的: このコマンドは『create_model_arc』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_model_arc

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| center | object | いいえ/状況による |  |
| end | object | いいえ/状況による |  |
| endAngleDeg | number | いいえ/状況による |  |
| mid | object | いいえ/状況による |  |
| mode | string | いいえ/状況による | three_point |
| radiusMm | number | いいえ/状況による |  |
| start | object | いいえ/状況による |  |
| startAngleDeg | number | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_model_arc",
  "params": {
    "center": "...",
    "end": "...",
    "endAngleDeg": 0.0,
    "mid": "...",
    "mode": "...",
    "radiusMm": 0.0,
    "start": "...",
    "startAngleDeg": 0.0
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