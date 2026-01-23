# path.waypoints.set

- カテゴリ: Route
- 目的: `PathOfTravel` 要素のウェイポイントを全置換します。

## 概要
既存の `PathOfTravel` に対してウェイポイント（点列）を置き換え、既定では再計算（`Update()`）します。

## 使い方
- メソッド: `path.waypoints.set`
- トランザクション: Write

### パラメータ
| 名前 | 型 | 必須 | 既定値 | 補足 |
|---|---|---:|---|---|
| pathId | int | はい |  | `PathOfTravel` の要素ID |
| waypoints | point[] | はい |  | 点は mm |
| update | bool | いいえ | true | true の場合 `PathOfTravel.Update()` を実行 |

補足:
- `elementId` も `pathId` の別名として受け付けます。
- 可能であれば Z はオーナービューのレベル標高にスナップされます。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": "wp-set-1",
  "method": "path.waypoints.set",
  "params": {
    "pathId": 999888,
    "update": true,
    "waypoints": [
      { "x": 2000, "y": 3000, "z": 0 },
      { "x": 4000, "y": 3000, "z": 0 }
    ]
  }
}
```

## 戻り値
- `data.waypointsApplied`: int
- `data.updated`: bool
- `data.status`: string（`PathOfTravelCalculationStatus`）

