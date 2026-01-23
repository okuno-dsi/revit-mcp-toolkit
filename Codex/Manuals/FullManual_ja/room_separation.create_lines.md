# room_separation.create_lines

- カテゴリ: Room
- 目的: 平面ビューで、線分配列から Room Separation Lines（部屋境界線）を作成します。

## 概要
平面ビュー（床プラン / 天井プラン）上に、**Room Separation Lines（部屋境界線）** を複数本まとめて作成します。

壁を作らずに部屋境界を作りたい場合や、一時的に境界をコントロールしたい場合に便利です。

## 使い方
- メソッド: `room_separation.create_lines`
- トランザクション: Write

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---:|---|
| viewId | int | いいえ | ActiveView |
| unit | string | いいえ | `"mm"` |
| snapZToViewLevel | bool | いいえ | `true` |
| segments | object[] | はい（推奨） |  |
| start | object | legacy |  |
| end | object | legacy |  |

点（座標）の形式:
```json
{ "x": 0.0, "y": 0.0, "z": 0.0 }
```

線分の形式:
```json
{ "start": { "x": 0, "y": 0, "z": 0 }, "end": { "x": 1000, "y": 0, "z": 0 } }
```

補足:
- `segments[]` の指定を推奨します。`segments[]` がある場合、`start/end` は無視されます。
- `unit` は `"mm"` / `"m"` / `"ft"` を受け付けます（既定 `"mm"`）。
- `snapZToViewLevel=true` の場合、端点のZは **ビューのレベル標高** にスナップされます。
- ゼロ長さの線分は作成せず、`result.skipped[]` に理由付きで返します。

### リクエスト例（segments）
```json
{
  "jsonrpc": "2.0",
  "id": "rs-1",
  "method": "room_separation.create_lines",
  "params": {
    "viewId": 123456,
    "unit": "mm",
    "snapZToViewLevel": true,
    "segments": [
      { "start": { "x": 0, "y": 0, "z": 0 }, "end": { "x": 5000, "y": 0, "z": 0 } },
      { "start": { "x": 5000, "y": 0, "z": 0 }, "end": { "x": 5000, "y": 3000, "z": 0 } }
    ]
  }
}
```

## 戻り値
- `ok`: boolean
- `result.created`: 作成した部屋境界線の要素ID（int[]）
- `result.count`: 本数（int）
- `result.skipped[]`: 作成しなかった線分（`{ index, reason }`）

## 関連
- `create_room_boundary_line`（本コマンドの deprecated alias）

