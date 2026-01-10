# 部屋境界に仕上げ壁タイプを適用（セグメント長）

選択した `Room` の境界を基準に、**部屋側**に「重ね壁（仕上げ壁）」として新しい壁を作成します。  
このコマンドは **部屋境界のセグメント長**で作成するため、ホスト壁が長くて複数の部屋にまたがる場合でも「選択した部屋の辺の範囲だけ」を作れます。

## コマンド
- Canonical: `room.apply_finish_wall_type_on_room_boundary`
- 別名: `apply_finish_wall_type_on_room_boundary`

## パラメータ
| 名前 | 型 | 必須 | 既定 | メモ |
|---|---|---:|---:|---|
| newWallTypeNameOrId | string | yes |  | 作成する `WallType`（名前推奨、または elementId 文字列）。 |
| roomId | int | no |  | 省略時は選択から取得（`fromSelection=true`）。 |
| fromSelection | bool | no | auto | 選択中の `Room` をターゲットにします。 |
| boundaryLocation | string | no | Finish | `Finish`/`Center`/`CoreCenter`/`CoreBoundary`。 |
| includeIslands | bool | no | false | 内部ループ（島）も処理します。 |
| minSegmentLengthMm | number | no | 1.0 | 極端に短いセグメントをスキップします。 |
| onlyRcCore | bool | no | true | `true` の場合、**コア層**のマテリアル名が `coreMaterialNameContains` に一致する壁のみ対象。 |
| coreMaterialNameContains | string | no | *コンクリート | コア層マテリアル名の部分一致トークン（区切り: `,`/`;`/`|`）。 |
| includeBoundaryColumns | bool | no | true | `true` の場合、境界要素が「柱（構造柱/建築柱）」の境界セグメントも対象にします。 |
| skipExisting | bool | no | true | 既に同タイプの仕上げ壁が部屋側にあると推定できる場合、そのセグメントは作成しません（best-effort）。 |
| existingMaxOffsetMm | number | no | 120.0 | 境界セグメントと既存仕上げ壁の距離（2D）許容。 |
| existingMinOverlapMm | number | no | 100.0 | 2Dでの重なり長さ（最小）。 |
| existingMaxAngleDeg | number | no | 3.0 | 平行判定の角度許容（deg）。 |
| existingSearchMarginMm | number | no | 2000.0 | 候補壁探索の拡張幅（XY）。 |
| heightMm | number | no |  | 作成壁の「指定高さ」上書き（未指定なら部屋の高さを使用）。 |
| baseOffsetMm | number | no |  | 作成壁の基準レベルオフセット上書き。 |
| roomBounding | bool | no | false | 既定は作成壁を `Room Bounding=OFF` にします。`true` にすると変更しません。 |
| probeDistMm | number | no | 200.0 | “部屋内側”の判定用プローブ距離（best-effort）。 |
| joinEnds | bool | no | true | 作成壁の端部結合を Allow（結合ON）にします（best-effort）。 |
| joinType | string | no | miter | `miter`/`butt`/`squareoff`（best-effort）。 |

## 例（JSON-RPC）
選択中の部屋について、RCコア壁のみ `(内壁)W5` を作成：
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "room.apply_finish_wall_type_on_room_boundary",
  "params": {
    "fromSelection": true,
    "newWallTypeNameOrId": "(内壁)W5",
    "onlyRcCore": true,
    "coreMaterialNameContains": "*コンクリート",
    "boundaryLocation": "Finish",
    "skipExisting": true,
    "joinEnds": true
  }
}
```

## 戻り値（形）
`ok=true` のとき `result` に以下を返します：
- `createdWallIds`（`int[]`）, `createdCount`
- `segments[]`：境界セグメント単位の結果（`loopIndex`, `segmentIndex`, `boundaryElementId`, `boundaryElementKind`, `hostWallId`/`hostColumnId`, `status`, 必要に応じて `createdWallId`）
- `skipped[]`：壁以外の境界/条件不一致など

## 補足
- `element.create_flush_walls` は **ソース壁の LocationCurve 全長**を基準に作成しやすいのに対し、このコマンドは **部屋境界セグメント長**で作成します。
- 作成壁は「境界曲線＋壁厚（半分）オフセット」で中心線を作って生成します（best-effort）。
- 柱の境界セグメントは、柱が Room Bounding として境界に出ている場合にのみ検出されます。柱が拾われない場合は、柱の `Room Bounding` 設定や部屋境界設定を確認してください。
