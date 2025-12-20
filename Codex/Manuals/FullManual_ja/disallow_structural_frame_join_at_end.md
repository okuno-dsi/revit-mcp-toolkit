# disallow_structural_frame_join_at_end

- カテゴリ: ElementOps / StructuralFrame
- 目的: 構造フレーム要素（梁・ブレース）の端部結合を無効化します（`StructuralFramingUtils.DisallowJoinAtEnd` のラッパー）。

## 概要
このコマンドは JSON-RPC で Revit MCP アドインに送信され、指定した構造フレーム要素について、端部 0 / 1 の「結合を許可しない」フラグを立てます。  
`move_structural_frame` などで梁を移動する前に実行することで、Revit 側の結合ダイアログが出るのを抑制する用途を想定しています。

## 使い方
- メソッド: `disallow_structural_frame_join_at_end`

### パラメータ
| 名前        | 型      | 必須                    | 既定値        |
|-------------|---------|-------------------------|---------------|
| elementIds  | int[]   | いいえ / いずれか一方   | []            |
| elementId   | int     | いいえ / いずれか一方   | 0             |
| uniqueId    | string  | いいえ / いずれか一方   |               |
| endIndex    | int     | いいえ                  | （下記参照）  |
| ends        | int[]   | いいえ                  | （下記参照）  |

- `elementIds` / `elementId` / `uniqueId` のどれか一つは必ず指定してください。
- `endIndex` / `ends`:
  - 有効値は `0` または `1`（始端 / 終端）です。
  - どちらも指定しない場合は、両端 `[0, 1]` が対象になります。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "disallow_structural_frame_join_at_end",
  "params": {
    "elementIds": [5938155, 5938694],
    "ends": [0, 1]
  }
}
```

### 結果の例
```json
{
  "ok": true,
  "requested": 2,
  "processed": 2,
  "changedEnds": 4,
  "skipped": 0,
  "notFraming": 0,
  "failed": 0
}
```

## メモ
- 対象は構造フレームの FamilyInstance で、`StructuralType` が `Beam` または `Brace` のものだけです。
- 幾何学的な JoinGeometry の解除は行いません。必要に応じて:
  1. `get_joined_elements` で結合相手を調べる
  2. `unjoin_elements` で JoinGeometry を解除する
  3. `disallow_structural_frame_join_at_end` で端部結合を禁止する  
  といった手順で組み合わせてください。
- その後 `move_structural_frame` で梁を移動すると、Revit UI の結合ダイアログが出にくくなります。

## 関連コマンド
- move_structural_frame
- get_joined_elements
- unjoin_elements
- unpin_element
