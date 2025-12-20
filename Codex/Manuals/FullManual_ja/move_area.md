# move_area

- カテゴリ: Area
- 目的: このコマンドは『move_area』を移動します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: move_area

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| batchSize | int | いいえ/状況による | 50 |
| dx | number | いいえ/状況による |  |
| dy | number | いいえ/状況による |  |
| dz | number | いいえ/状況による |  |
| maxMillisPerTx | int | いいえ/状況による | 100 |
| refreshView | bool | いいえ/状況による | false |
| startIndex | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "move_area",
  "params": {
    "batchSize": 0,
    "dx": 0.0,
    "dy": 0.0,
    "dz": 0.0,
    "maxMillisPerTx": 0,
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
- delete_area
- get_area_boundary_walls
- get_area_centroid
- 