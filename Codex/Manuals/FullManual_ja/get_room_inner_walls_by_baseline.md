# get_room_inner_walls_by_baseline

- カテゴリ: Room
- 目的: このコマンドは、指定した部屋の内部にある壁を『壁の基準線上のサンプル点』を使って検出します。

## 概要
このコマンドは JSON-RPC を通じて実行され、部屋 ID といくつかのオプションを指定すると、

- 同じレベル、またはそのレベルを跨ぐ壁のうち、
- 壁の LocationCurve 上の指定位置（例えば 0.33 と 0.67）の点が、
- 部屋の中間高さで `Room.IsPointInRoom` によって「室内」と判定されるもの

だけを「部屋内部の壁」として返します。  
平面図には現れない宙に浮いた壁（梁下の下がり壁など）も、XY 位置が室内であれば検出できます。

## 使い方
- メソッド: get_room_inner_walls_by_baseline

### パラメータ
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---|---|---|
| roomId | int | はい | なし | 対象の部屋の elementId |
| t1 | double | いいえ | 0.33 | 壁の LocationCurve 上のサンプル位置1（0.0〜1.0） |
| t2 | double | いいえ | 0.67 | 壁の LocationCurve 上のサンプル位置2（0.0〜1.0） |
| searchRadiusMm | double | いいえ | null | 部屋中心からの XY 半径（mm）。指定した場合、この範囲内の壁だけを対象とします |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_room_inner_walls_by_baseline",
  "params": {
    "roomId": 6808111,
    "t1": 0.33,
    "t2": 0.67
  }
}
```

### 応答イメージ
```json
{
  "ok": true,
  "result": {
    "roomId": 6808111,
    "levelId": 11815,
    "levelName": "2FL",
    "t1": 0.33,
    "t2": 0.67,
    "searchRadiusMm": null,
    "wallCount": 11,
    "walls": [
      {
        "wallId": 8362180,
        "uniqueId": "...",
        "typeId": 8338189,
        "typeName": "(内壁)W5",
        "levelId": 11815,
        "levelName": "2FL",
        "sample": {
          "t1": 0.33,
          "t2": 0.67,
          "p1": { "x": 21138.1, "y": -205.0, "z": 5319.2 },
          "p2": { "x": 22861.9, "y": -205.0, "z": 5319.2 }
        }
      }
    ]
  }
}
```

- `walls[*].sample.p1/p2` は、判定に使用したサンプル点（mm）です。
- 返ってきた `wallId` を使って `lookup_element` や `get_wall_parameters` などで詳細情報を取得できます。

## 関連コマンド
- get_rooms
- get_room_params
- get_room_boundary
- get_room_boundary_walls
- get_room_perimeter_with_columns_and_walls

