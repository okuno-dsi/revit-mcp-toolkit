# get_room_finish_takeoff_context

- カテゴリ: Room
- 目的: 部屋の仕上げ数量算出に必要な「境界線＋近傍壁＋柱＋ドア/窓（壁ホスト）」情報を、1回の呼び出しでまとめて取得します。

## 概要
- 部屋境界（`Room.GetBoundarySegments`）の各セグメント座標・長さ・境界要素IDを返します。
- 境界セグメントに対して近傍の壁を幾何学的にマッチングし、`segmentIndex` との対応を返します（許容距離/角度/重なりはパラメータで調整）。
- マッチした壁がホストするドア/窓（挿入要素）を壁ごとに整理して返します。
- 必要に応じて、柱（構造柱/建築柱）の検出と、柱↔壁の近接（BoundingBox近似）も返します。

※ 英語版: `../FullManual/get_room_finish_takeoff_context.md`

## 注意
- `metrics.estWallAreaToCeilingM2` は `wallPerimeterMm × roomHeightMm` の概算で、開口（ドア/窓）控除は現時点では行いません。
- `boundaryKind=Wall/Column/Other` を見て、壁周りと柱周りの区別に利用してください。
- `tempEnableRoomBoundingOnColumns=true` の場合、柱の「Room Bounding」を一時的にONにして境界を計算しますが、`TransactionGroup.RollBack()` によりモデルは変更されません。

## 使い方
- Method: `get_room_finish_takeoff_context`

### パラメータ（主要）
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---:|---:|---|
| roomId | int | no |  | 未指定の場合は選択から解決します（`fromSelection=true`）。 |
| fromSelection | bool | no | auto | 選択中の `Room` / `RoomTag` を対象にします。 |
| boundaryLocation | string | no | Finish | `Finish` / `Center` / `CoreCenter` / `CoreBoundary` |
| includeSegments | bool | no | true | `loops[].segments[]`（座標・長さ）を返します。 |
| includeWallMatches | bool | no | true | 境界線に近い壁のマッチングを行います。 |
| includeInserts | bool | no | true | マッチした壁がホストするドア/窓を返します。 |
| autoDetectColumnsInRoom | bool | no | false | 部屋内の柱を自動検出します。 |
| tempEnableRoomBoundingOnColumns | bool | no | true | 柱の `Room Bounding=ON` を一時的に行います（ロールバックされます）。 |
| includeFloorCeilingInfo | bool | no | true | 床/天井の関連要素を収集します（bbox＋室内サンプル点）。 |
| floorCeilingSearchMarginMm | number | no | 1000.0 | 候補探索範囲（部屋境界bboxをXY方向に拡張）。 |
| segmentInteriorInsetMm | number | no | 100.0 | 境界線から部屋内側へずらすサンプル距離。 |

## 戻り値メモ（主要フィールド）
- 周長の比較:
  - `metrics.perimeterParamMm` / `metrics.perimeterParamDisplay`: Revitの「周長」パラメータ
  - `metrics.perimeterMm`: コマンドが境界線セグメントから算出した実測値
- レベル/オフセット:
  - `room.levelElevationMm`
  - `room.baseOffsetMm` / `room.baseOffsetDisplay`
- 床/天井（仕上げ数量の前処理用の参考情報）:
  - `floors[]`: `topHeightFromRoomLevelMm`（床は上面）
  - `ceilings[]`: `heightFromRoomLevelMm`
  - `ceilingIdsBySegmentKey`: `"loopIndex:segmentIndex"` → `[ceilingId,…]`（近似）
  - `loops[].segments[].ceilingHeightsFromRoomLevelMm`: セグメント単位の天井高さサンプル（`includeSegments=true` の場合）

## Excel（セグメント表）
比較JSON（`test_room_finish_takeoff_context_compare_*.json`）から、標準化した `SegmentWallTypes` シートを再生成するスクリプト:
- `Manuals/Scripts/room_finish_compare_to_excel.py`


