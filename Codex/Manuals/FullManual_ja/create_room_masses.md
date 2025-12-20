# create_room_masses

- カテゴリ: Room
- 目的: このコマンドは複数の Room から、外形線と高さに合わせた DirectShape Mass をまとめて生成します。

## 概要
`create_room_masses` は、Room の外周境界と高さをもとに、部屋ボリュームと対応するインプレイスマス（DirectShape）を一括で作成するコマンドです。  
安全のため、対象 Room 数の上限（`maxRooms`）や不正形状の検知を行い、問題のある部屋は `issues.itemErrors` に記録してスキップします。

## 使い方
- メソッド: create_room_masses

### パラメータ
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---|---|---|
| roomIds | int[] | いいえ |  | 対象とする Room の `elementId` 配列。指定がある場合はこれが最優先。 |
| viewId | int | いいえ | 0 | `roomIds` がない場合に使用。指定ビューに可視な Room を対象とする。 |
| maxRooms | int | いいえ | 200 | 処理対象の Room 数上限。超える場合はエラーを返して処理を行わない。 |
| heightMode | string | いいえ | "bbox" | マス高さの決定方法。 `"bbox"`: Room の BoundingBox 高さ, `"fixed"`: 固定高さ（`fixedHeightMm`）を使用。 |
| fixedHeightMm | number | いいえ | 2800.0 | `heightMode` が `"fixed"` の場合、または `bbox` 高さが 0 の場合に使用する高さ（mm）。 |
| useMassCategory | bool | いいえ | true | `true` で Mass カテゴリ (OST_Mass)、`false` で 汎用モデル (OST_GenericModel) として作成。 |

### リクエスト例（ビュー内の Room からマスを一括生成）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_room_masses",
  "params": {
    "viewId": 1332698,
    "maxRooms": 50,
    "heightMode": "bbox",
    "fixedHeightMm": 2800.0,
    "useMassCategory": true
  }
}
```

### レスポンス例（抜粋）
```json
{
  "ok": true,
  "createdCount": 27,
  "created": [
    { "roomId": 6806608, "massId": 1234567 }
  ],
  "units": { "Length": "mm", "Area": "m2", "Volume": "m3", "Angle": "deg" },
  "issues": {
    "itemErrors": [
      { "roomId": 999, "message": "Room 境界が取得できませんでした。（Not enclosed / Unplaced の可能性）" },
      { "roomId": 1000, "message": "Room 外周ループが平面上にありません。" }
    ]
  }
}
```

## 注意事項
- `maxRooms` を超える場合は実行されず、エラーメッセージと対象数が返されます。
- 各 Room ごとの処理は独立しており、1 部屋で失敗しても他の部屋のマス生成は継続されます。
- Room の BoundingBox 高さが 0 の場合や、外周ループが閉じていない／平面上でない場合は、その Room はスキップされます。

## 関連コマンド
- get_room_boundaries
- get_room_boundary
- get_room_perimeter_with_columns
- get_mass_instances

