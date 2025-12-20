# rejoin_walls

- カテゴリ: ElementOps / Wall
- 目的: 壁要素の端部結合を再許可し、可能であれば結合タイプ（Butt/Miter/SquareOff）を設定し、必要に応じてジオメトリ結合も再設定します。

## 概要
このコマンドは JSON-RPC で Revit MCP アドインに送信され、複数の壁インスタンスに対して次の処理を行います:

- `WallUtils.AllowWallJoinAtEnd(w, 0/1)` により、両端の「結合を許可」フラグを立てる。
- `LocationCurve.JoinType[end]` に指定された結合タイプ（Butt/Miter/SquareOff）を設定（端部に結合が存在する場合）。
- Butt / SquareOff の場合、`prefer_wall_id` で指定した壁を優先して結合順序を入れ替える（`ElementsAtJoin[end]` の先頭に移動）。
- `also_join_geometry` が true の場合、`LocationCurve.ElementsAtJoin[end]` にいる隣接壁に対して `JoinGeometryUtils.JoinGeometry(doc, wall, neighbor)` を呼び出し、ジオメトリ結合も再設定する。

Revit UI の「壁結合」ダイアログで行う操作を、MCP からある程度まとめて行うための補助コマンドです。

## 使い方
- メソッド: `rejoin_walls`

### パラメータ
| 名前              | 型      | 必須                 | 既定値      |
|-------------------|---------|----------------------|-------------|
| wall_ids          | int[]   | はい                 | []          |
| elementIds        | int[]   | いいえ / エイリアス | []          |
| join_type         | string  | いいえ               | `"butt"`    |
| prefer_wall_id    | int     | いいえ               | null        |
| also_join_geometry | bool   | いいえ               | false       |

- `wall_ids` と `elementIds` は同じ意味です。どちらか一方に壁の要素IDを指定してください。
- `join_type`:
  - `"butt"` → `JoinType.Abut`
  - `"miter"` → `JoinType.Miter`
  - `"square"` / `"squareoff"` → `JoinType.SquareOff`
  - 省略または未知の文字列 → `"butt"` として扱われます。
- `prefer_wall_id`（任意）:
  - Butt / SquareOff の場合に、`LocationCurve.ElementsAtJoin[end]` の並び順を変更し、この壁を先頭に持ってくることで「どの壁を優先して接合するか」を指定できます。
- `also_join_geometry`:
  - true の場合、壁 join の情報をもとにジオメトリレベルでの JoinGeometry も再適用します。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "rejoin_walls",
  "params": {
    "wall_ids": [6798115, 6798116],
    "join_type": "miter",
    "prefer_wall_id": 6798115,
    "also_join_geometry": true
  }
}
```

### 結果の例
```json
{
  "ok": true,
  "requested": 2,
  "processed": 2,
  "notWall": 0,
  "noLocation": 0,
  "failed": 0,
  "joinType": "Miter",
  "preferWallId": null,
  "alsoJoinGeometry": true,
  "joinTypeSetCount": 3,
  "reorderCount": 0,
  "geometryJoinCount": 0,
  "results": [
    {
      "elementId": 6798115,
      "status": "ok",
      "joinTypeSetAtEnds": 2,
      "reorderedEnds": 0,
      "geometryJoins": 0
    },
    {
      "elementId": 6798116,
      "status": "ok",
      "joinTypeSetAtEnds": 1,
      "reorderedEnds": 0,
      "geometryJoins": 0
    }
  ]
}
```

## メモ
- 壁（`Autodesk.Revit.DB.Wall`）以外の要素IDは処理されず、`notWall` にカウントされます。
- `noLocation` は `LocationCurve` を持たない壁の件数です（通常の壁ではほとんど発生しません）。
- `joinTypeSetCount` は `JoinType` の設定に成功した端部の総数です。Revit の仕様により、全ての端部で要求どおりに設定できるとは限りません。
- `geometryJoinCount` は `JoinGeometryUtils.JoinGeometry` の呼び出しが成功した回数です。

## 関連コマンド
- disallow_wall_join_at_end
- join_elements
- unjoin_elements
