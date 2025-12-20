# get_room_perimeter_with_columns

- ジャンル: Room
- 目的: 指定（または自動検出）した柱を一時的に Room Bounding として扱いながら、部屋の周長をミリメートル単位で取得し、必要に応じて周長を構成する線分座標も取得します。

## 概要
このコマンドは Revit MCP アドインに対して JSON-RPC で実行します。
指定された `columnIds` の柱を一時的に「Room Bounding = ON」に切り替えて再生成したあと、
組み込みパラメータ `ROOM_PERIMETER` または `Room.GetBoundarySegments` によって周長を計算します。
すべての変更は `TransactionGroup.RollBack()` で元に戻されるため、モデル自体は変更されません。
オプションで、周長を構成するループと線分座標をミリ単位で取得し、後続処理（例: 周長近傍の壁検出）に利用できます。

## 使い方
- メソッド: `get_room_perimeter_with_columns`

### パラメータ
| 名前                               | 型        | 必須 | 既定値    | 説明 |
|------------------------------------|-----------|----:|-----------|------|
| `roomId`                           | integer   | 必須 | ?         | 対象となる `Room` の ElementId。 |
| `columnIds`                        | integer[] | 任意 | `[]`      | 一時的に Room Bounding = ON にする柱の ElementId 配列。 |
| `autoDetectColumnsInRoom`          | boolean   | 任意 | `false`   | `true` の場合、部屋と交差している建築柱 / 構造柱を自動検出し、`columnIds` にマージします。 |
| `searchMarginMm`                   | number    | 任意 | `1000.0`  | `autoDetectColumnsInRoom` が `true` のときに使用する、部屋の BoundingBox を外側に広げる距離（mm）。 |
| `includeIslands`                   | boolean   | 任意 | `true`    | `true` の場合、部屋境界のすべてのループ（島を含む）を周長に含めます。`false` の場合は外周ループのみを使用します。 |
| `includeSegments`                  | boolean   | 任意 | `false`   | `true` の場合、結果に `loops` としてループ・線分座標（mm）を含めます。 |
| `boundaryLocation`                 | string    | 任意 | `"Finish"` | `SpatialElementBoundaryOptions` の境界位置指定。`"Finish"`, `"Center"`, `"CoreCenter"`, `"CoreFace"` など。 |
| `useBuiltInPerimeterIfAvailable`   | boolean   | 任意 | `true`    | 利用可能な場合は組み込みの `ROOM_PERIMETER` パラメータを優先し、そうでなければ境界線から周長を算出します。 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_room_perimeter_with_columns",
  "params": {
    "roomId": 123456,
    "columnIds": [234567, 234568],
    "autoDetectColumnsInRoom": true,
    "searchMarginMm": 1000.0,
    "includeIslands": true,
    "includeSegments": true,
    "boundaryLocation": "Finish",
    "useBuiltInPerimeterIfAvailable": true
  }
}
```

### レスポンス例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "roomId": 123456,
    "perimeterMm": 24850.0,
    "loops": [
      {
        "loopIndex": 0,
        "segments": [
          {
            "start": { "x": 1000.0, "y": 2000.0, "z": 0.0 },
            "end":   { "x": 5000.0, "y": 2000.0, "z": 0.0 }
          }
          // ... 他の線分
        ]
      }
      // ... includeIslands = true の場合、他のループも続く
    ],
    "units": {
      "Length": "mm"
    },
    "basis": {
      "includeIslands": true,
      "boundaryLocation": "Finish",
      "useBuiltInPerimeterIfAvailable": true,
      "autoDetectColumnsInRoom": true,
      "searchMarginMm": 1000.0,
      "autoDetectedColumnIds": [345001, 345002],
      "toggledColumnIds": [234567, 234568, 345001, 345002]
    }
  }
}
```

## 関連コマンド
- get_room_boundary
- get_room_boundary_walls
- get_room_centroid
- get_room_planar_centroid
- summarize_rooms_by_level

