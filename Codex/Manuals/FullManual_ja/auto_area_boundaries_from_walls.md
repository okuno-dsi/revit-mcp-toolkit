# auto_area_boundaries_from_walls

- カテゴリ: Area
- 目的: このコマンドは『auto_area_boundaries_from_walls』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: auto_area_boundaries_from_walls

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| batchSize | int | いいえ/状況による | 200 |
| includeCategories | unknown | いいえ/状況による |  |
| maxMillisPerTx | int | いいえ/状況による | 250 |
| mergeToleranceMm | number | いいえ/状況による | 5.0 |
| refreshView | bool | いいえ/状況による | false |
| startIndex | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "auto_area_boundaries_from_walls",
  "params": {
    "batchSize": 0,
    "includeCategories": "...",
    "maxMillisPerTx": 0,
    "mergeToleranceMm": 0.0,
    "refreshView": false,
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