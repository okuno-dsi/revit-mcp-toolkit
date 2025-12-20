# create_areas_from_rooms_by_material_corecenter

- カテゴリ: Area
- 目的: 1回の呼び出しで、選択したRoomの境界→エリア境界線作成→Area作成→指定マテリアル層中心（コア中心）へ境界調整をまとめて実行します。

## 概要
このコマンドは **高速化** と **手順の簡略化** のために用意しています。従来のように複数コマンドを順に呼ぶ（`copy_area_boundaries_from_rooms` → `get_room_centroid` → `create_area` → `area_boundary_adjust_by_material_corecenter` → `get_area_metrics`）代わりに、代表的なワークフローを1回のMCPリクエストで実行します。

**エリア平面（Area Plan）** で実行してください。Area Plan と **同じレベル** のRoomのみが対象になります。

主な挙動:
- 既定では `Room.GetBoundarySegments()`（計算境界）からエリア境界線を作成します。
- `options.boundaryCurveSource=PreferLineElements` の場合、`BoundarySegment.ElementId` が `CurveElement`（例: Room Separation Line）を指している区間は、線要素の形状を優先して扱います。ただし境界ループが閉じない（Area=0 になる）ことを避けるため、端点は `BoundarySegment` 側の端点に合わせてクリップします（線要素を参照しつつ、セグメントの始点・終点を尊重します）。
- Areaの配置点は Room の **LocationPoint（高速）** を優先し、無い場合は **ソリッド重心（低速）** にフォールバック
- 境界調整は既存の `area_boundary_adjust_by_material_corecenter` のロジックを利用
- `refreshView` は最後に **1回だけ**（任意）
- 堅牢性のため、調整ステップは `options.fallback_to_core_centerline=true` を既定で有効化します（明示指定がある場合はそれに従います）。
- Room境界の取得位置は `options.boundaryLocation` / `boundaryLocation` で指定できます（既定: `CoreCenter`）。

## ユーザー確認ポイント（実行前）
- 「境界線」と言われた場合は、A) 計算境界（`Room.GetBoundarySegments`）か、B) 線要素（Room/Area Separation Lines 等）かを確認してください（`Manuals/FullManual_ja/spatial_boundary_location.md`）。
- `boundaryLocation`（`Finish`/`Center`/`CoreCenter`/`CoreBoundary`）は用途により変わるため確認してください（内法か、壁芯/コア芯か）。
- 「面積をRoomと一致させたい」場合は、まず `options.adjust_boundaries=false` で計算境界をそのまま使うのが安全です（調整すると面積差が出る可能性があります）。
- 「指定マテリアル層中心（コア中心）へ寄せたい」場合は `options.adjust_boundaries=true` にし、`materialId/materialName` を指定してください（時間がかかることがあります）。
- 既存のエリア境界線が多いビューでは、想定外の閉じ方（Area=0、広がりすぎ等）が起きることがあるため、専用のArea Plan/Area Schemeで実行するか事前整理を推奨します。

## 注意点（よくある原因）
- 選択: **Room** だけでなく **RoomTag（部屋タグ）** を選択しても動作します（RoomTag はホストする Room に解決して処理します）。
- ビュー切替などで選択がすぐ消える場合は、直近の **最後の非空選択（last non-empty selection）** にフォールバックできます（短時間のみ）。`options.selection_stash_max_age_ms` を参照してください。
- 既にエリア境界線が大量にある Area Plan では、`options.skip_existing=true` により既存線を再利用します（重なり線を作らないため安全）。ただし既存の境界ネットワークが繋がっていると、作成した Area が「閉じていない（面積0）」や、想定外に広い領域になることがあります。その場合は、空の Area Plan（ビュー複製／専用 Area Scheme）で実行するか、対象周辺の不要なエリア境界線を整理してから実行してください。
- 部屋境界が不整形（短い分割、円弧、微小なズレ等）の場合は `Room.GetBoundarySegments()` の結果に従うため、最終的に人手での微調整（トリム／延長）が必要になることがあります。
- `options.adjust_boundaries=true` の場合、境界線は「壁・マテリアル層」を使って調整され、境界線IDが作り直されることがあります（`adjustResult.adjustedBoundaryLineIdMap` を参照）。
- 調整ステップは処理負荷が高くなりやすいです。必要に応じて `startIndex/batchSize` で分割実行してください。

## 使い方
- メソッド: create_areas_from_rooms_by_material_corecenter

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---:|:---:|---|
| materialId / materialName / material | (int / string / object) | 状況による* |  |
| viewId / viewUniqueId | (int / string) | いいえ | アクティブビュー |
| room_element_ids | int[] | いいえ | 選択（同レベルのRoomのみ） |
| wall_element_ids | int[] | いいえ | 選択（壁のみ）/自動収集** |
| refreshView | bool | いいえ | false |
| startIndex | int | いいえ | 0 |
| batchSize | int | いいえ | 非常に大きい値（全件） |
| options.copy_boundaries | bool | いいえ | true |
| options.create_areas | bool | いいえ | true |
| options.adjust_boundaries | bool | いいえ | true |
| options.collect_walls_from_room_boundaries | bool | いいえ | true |
| options.skip_existing | bool | いいえ | true |
| options.mergeToleranceMm | number | いいえ | 3.0 |
| options.return_area_metrics | bool | いいえ | true |
| options.regenerate_before_metrics | bool | いいえ | true |
| options.include_debug | bool | いいえ | false |
| options.selection_stash_max_age_ms | int | いいえ | 5000 |
| options.boundaryLocation | string | いいえ | `CoreCenter` |
| options.boundaryCurveSource | string | いいえ | `BoundarySegment` |
| options.preferLineElements | bool | いいえ |（指定時は `boundaryCurveSource` を上書き）|
| options.compare_room_and_area | bool | いいえ | true |
| boundaryLocation | string | いいえ |（`options.boundaryLocation` と同じ）|

*`options.adjust_boundaries=true` の場合は `materialId` / `materialName` / `material:{id|name}` のいずれかを指定してください（`adjust_boundaries=false` の場合は不要です）。

**`wall_element_ids` を指定せず、選択にも壁が無い場合、Room境界から壁を自動収集できます（`options.collect_walls_from_room_boundaries=true`）。

### 調整ステップへのオプション引き継ぎ（`area_boundary_adjust_by_material_corecenter`）
次のオプションは `options` に指定すると調整処理へ引き継がれます:
- `tolerance_mm` / `toleranceMm`
- `corner_tolerance_mm` / `cornerToleranceMm`
- `match_strategy` / `matchStrategy`
- `corner_resolution` / `cornerResolution`
- `parallel_threshold` / `parallelThreshold`
- `include_noncore` / `includeNonCore`
- `include_layer_details` / `includeLayerDetails`
- `include_debug` / `includeDebug`
- `fallback_to_core_centerline` / `fallbackToCoreCenterline`

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_areas_from_rooms_by_material_corecenter",
  "params": {
    "viewId": 11120260,
    "material": { "name": "*コンクリート" },
    "room_element_ids": [11121453, 11121539, 11121675],
    "refreshView": true,
    "options": {
      "mergeToleranceMm": 3.0,
      "tolerance_mm": 5.0,
      "corner_resolution": "trim_extend",
      "match_strategy": "nearest_parallel",
      "collect_walls_from_room_boundaries": true,
      "return_area_metrics": true
    }
  }
}
```

#### 計算境界そのまま（Room面積と一致させたい場合）
`options.adjust_boundaries=false` とし、`material` を省略できます:
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_areas_from_rooms_by_material_corecenter",
  "params": {
    "options": {
      "adjust_boundaries": false,
      "boundaryCurveSource": "BoundarySegment",
      "boundaryLocation": "CoreCenter"
    }
  }
}
```

## 結果（概要）
- `createdAreaIds`: 作成されたAreaのID
- `createdBoundaryLineIds`: 作成されたエリア境界線ID
- `roomResults`: Roomごとの結果サマリ（作成AreaID、境界線の作成/スキップ数、Room面積とArea面積の比較など）
- `areaRoomComparisonSummary`: Room↔Area の面積差（平均/最大）のサマリ
- `areaMetrics`:（任意）作成したAreaの面積(m²)と周長(mm)
- `adjustResult`: `area_boundary_adjust_by_material_corecenter` の実行結果（そのまま）
- `timingsMs`: ステップ別の所要時間（copy/create/adjust/metrics/total）
- `warnings`: スキップ理由など
