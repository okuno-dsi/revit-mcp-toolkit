# clean_area_boundaries

- カテゴリ: Area
- 目的: このコマンドは『clean_area_boundaries』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: clean_area_boundaries

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| batchSize | int | いいえ/状況による |  |
| deleteIsolated | bool | いいえ/状況による | true |
| extendToleranceMm | number | いいえ/状況による | 50.0 |
| maxMillisPerTx | int | いいえ/状況による | 0 |
| mergeToleranceMm | number | いいえ/状況による | 5.0 |
| refreshView | bool | いいえ/状況による | false |
| stage | string | いいえ/状況による | all |
| startIndex | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "clean_area_boundaries",
  "params": {
    "batchSize": 0,
    "deleteIsolated": false,
    "extendToleranceMm": 0.0,
    "maxMillisPerTx": 0,
    "mergeToleranceMm": 0.0,
    "refreshView": false,
    "stage": "...",
    "startIndex": 0
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