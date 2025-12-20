# update_area

- カテゴリ: Area
- 目的: このコマンドは『update_area』を更新します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: update_area

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| batchSize | int | いいえ/状況による | 50 |
| maxMillisPerTx | int | いいえ/状況による | 100 |
| paramName | string | いいえ/状況による |  |
| refreshView | bool | いいえ/状況による | false |
| startIndex | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_area",
  "params": {
    "batchSize": 0,
    "maxMillisPerTx": 0,
    "paramName": "...",
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
- move_area
- delete_area
- get_area_boundary_walls
- get_area_centroid
- 