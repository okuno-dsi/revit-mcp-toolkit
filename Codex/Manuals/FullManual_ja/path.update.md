# path.update

- カテゴリ: Route
- 目的: 既存の `PathOfTravel` 要素を再計算します。

## 使い方
- メソッド: `path.update`
- トランザクション: Write

### パラメータ
| 名前 | 型 | 必須 | 補足 |
|---|---|---:|---|
| pathId | int | はい | `PathOfTravel` の要素ID |

補足:
- `elementId` も `pathId` の別名として受け付けます。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": "path-update-1",
  "method": "path.update",
  "params": { "pathId": 999888 }
}
```

## 戻り値
- `data.status`: string（`PathOfTravelCalculationStatus`）

