# extend_area_boundary_line

- カテゴリ: Area
- 目的: このコマンドは『extend_area_boundary_line』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: extend_area_boundary_line

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| batchSize | int | いいえ/状況による | 25 |
| lineId | int | いいえ/状況による |  |
| maxExtendMm | number | いいえ/状況による | 5000.0 |
| maxMillisPerTx | int | いいえ/状況による | 100 |
| refreshView | bool | いいえ/状況による | false |
| startIndex | int | いいえ/状況による | 0 |
| targetLineId | int | いいえ/状況による |  |
| toleranceMm | number | いいえ/状況による | 3.0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "extend_area_boundary_line",
  "params": {
    "batchSize": 0,
    "lineId": 0,
    "maxExtendMm": 0.0,
    "maxMillisPerTx": 0,
    "refreshView": false,
    "startIndex": 0,
    "targetLineId": 0,
    "toleranceMm": 0.0
  }
}
```

## 関連コマンド
## 関連コマンド
- create_area
- get_areas
- get_area_params
- update_area
- move_area
- delete_area
- get_area_boundary_walls
- 