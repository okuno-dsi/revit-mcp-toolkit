# copy_area_boundaries_from_rooms

- カテゴリ: Area
- 目的: このコマンドは『copy_area_boundaries_from_rooms』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: copy_area_boundaries_from_rooms

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| batchSize | int | いいえ/状況による | 50 |
| boundaryLocation | string | いいえ | `Finish` |
| maxMillisPerTx | int | いいえ/状況による | 200 |
| mergeToleranceMm | number | いいえ/状況による | 3.0 |
| refreshView | bool | いいえ/状況による | false |
| startIndex | int | いいえ/状況による | 0 |
| boundaryCurveSource | string | いいえ | `BoundarySegment` |
| preferLineElements | bool | いいえ |（指定時は `boundaryCurveSource` を上書き）|

`boundaryLocation` は `Room.GetBoundarySegments(...)` 実行時の `SpatialElementBoundaryLocation` を指定します:
- `Finish`（仕上げ面=内法）
- `Center`（壁芯）
- `CoreCenter`（コア芯/躯体芯）
- `CoreBoundary`（コア境界面）

注意:
- 既定では **A)**「計算されたRoom境界」を **エリア境界線** にコピーします。
- `boundaryCurveSource=PreferLineElements` の場合、境界セグメントが `CurveElement`（例: Room Separation Line）を参照している区間は、線要素の形状を優先して扱います。ただし、ループが閉じないことを避けるため端点は `BoundarySegment` 側の端点に合わせてクリップします（ハイブリッド）。
- **B)**「部屋境界線（Room Separation Lines）」そのもの（線要素）“だけ”をそのままコピーしたい場合（未閉鎖ループも含めて厳密に）は、`get_room_boundary_lines_in_view` + `create_area_boundary_line`（または CurveElement のコピー用スクリプト）を使ってください。
- `boundaryLocation` の既定値は `Finish`（内法）です。壁芯/コア芯で扱いたい場合は `Center`/`CoreCenter` を明示指定してください。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "copy_area_boundaries_from_rooms",
  "params": {
    "batchSize": 0,
    "boundaryLocation": "CoreCenter",
    "maxMillisPerTx": 0,
    "mergeToleranceMm": 0.0,
    "refreshView": false,
    "startIndex": 0
  }
}
```

## 関連コマンド
- create_area
- get_areas
- get_area_params
- update_area
- move_area
- delete_area
- get_area_boundary_walls
