# path.waypoints.get

- カテゴリ: Route
- 目的: `PathOfTravel` 要素に設定されているウェイポイントを取得します。

## 概要
`PathOfTravel` のウェイポイント（点列）を取得します。

## 使い方
- メソッド: `path.waypoints.get`
- トランザクション: Read

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
  "id": "wp-get-1",
  "method": "path.waypoints.get",
  "params": { "pathId": 999888 }
}
```

## 戻り値
- `data.waypoints[]`: ウェイポイント座標（mm, `{x,y,z}`）
- `data.count`: 個数

