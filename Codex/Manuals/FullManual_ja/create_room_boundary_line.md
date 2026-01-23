# create_room_boundary_line

- カテゴリ: Room
- 目的: `room_separation.create_lines`（部屋境界線作成）の deprecated alias です。

## 概要
互換性のため `create_room_boundary_line` は残していますが、新規実装は次を推奨します。

- `room_separation.create_lines`（canonical）

## 使い方
- メソッド: `create_room_boundary_line`（deprecated alias）
- Canonical: `room_separation.create_lines`
- トランザクション: Write

### パラメータ
`room_separation.create_lines` と同じです。

| 名前 | 型 | 必須 | 既定値 |
|---|---|---:|---|
| viewId | int | いいえ | ActiveView |
| unit | string | いいえ | `"mm"` |
| snapZToViewLevel | bool | いいえ | `true` |
| segments | object[] | はい（推奨） |  |
| start | object | legacy |  |
| end | object | legacy |  |

### リクエスト例（segments）
```json
{
  "jsonrpc": "2.0",
  "id": "rs-legacy-1",
  "method": "create_room_boundary_line",
  "params": {
    "viewId": 123456,
    "unit": "mm",
    "segments": [
      { "start": { "x": 0, "y": 0, "z": 0 }, "end": { "x": 5000, "y": 0, "z": 0 } }
    ]
  }
}
```

## 戻り値
- `result.created`: 作成した要素ID（int[]）
- `result.count`: 本数（int）
- `result.skipped[]`: 作成しなかった線分（`{ index, reason }`）

## 関連
- `room_separation.create_lines`
