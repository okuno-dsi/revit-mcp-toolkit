# get_room_boundaries

- カテゴリ: Room
- 目的: このコマンドは複数の Room 要素について、外形線（必要に応じて島ループを含む）を一括で取得します。

## 概要
`get_room_boundaries` は、1回の呼び出しで複数の部屋の境界線を mm 座標で取得するための読み取り専用コマンドです。  
`roomIds` を指定するか、`viewId` を指定してそのビュー内に可視な Room を対象にします。  
各 Room について、外周ループおよび（オプションで）島ループの線分を、`loops[].segments[].{start,end}` の形で返します。
トップレベルに `boundaryLocation` も返します（実際に使用した境界計算モード）。

## 使い方
- メソッド: get_room_boundaries

### パラメータ
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---|---|---|
| roomIds | int[] | いいえ |  | 対象とする Room の `elementId` 配列。指定がある場合はこれが最優先。 |
| viewId | int | いいえ | 0 | `roomIds` を指定しない場合に使用。指定ビューに可視な Room を対象とする。 |
| includeIslands | bool | いいえ | true | `true` で島ループも含めて返す。`false` の場合は外周ループのみ返す。 |
| boundaryLocation | string | いいえ | "Finish" | Room 境界の計算モード（例: `"Finish"`, `"Center"`, `"CoreCenter"`, `"CoreBoundary"` など）。 |

### リクエスト例（ビュー内の Room 境界を一括取得）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_room_boundaries",
  "params": {
    "viewId": 1332698,
    "includeIslands": true,
    "boundaryLocation": "Finish"
  }
}
```

### レスポンス例（抜粋）
```json
{
  "ok": true,
  "totalCount": 2,
  "rooms": [
    {
      "roomId": 6806608,
      "uniqueId": "8826...-0067dc50",
      "level": "2階",
      "loops": [
        {
          "loopIndex": 0,
          "segments": [
            {
              "start": { "x": 25060.0, "y": 15700.0, "z": 4100.0 },
              "end":   { "x": 28125.0, "y": 15700.0, "z": 4100.0 }
            }
          ]
        }
      ]
    }
  ],
  "units": { "Length": "mm", "Area": "m2", "Volume": "m3", "Angle": "deg" },
  "issues": {
    "itemErrors": [
      { "roomId": 999, "message": "Room 境界が取得できませんでした。（Not enclosed / Unplaced の可能性）" }
    ]
  }
}
```

## 関連コマンド
- get_room_boundary
- get_room_perimeter_with_columns
- get_room_perimeter_with_columns_and_walls
- get_rooms
