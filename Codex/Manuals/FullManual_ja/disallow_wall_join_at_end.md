# disallow_wall_join_at_end

- カテゴリ: ElementOps / Wall
- 目的: 壁要素の端部結合を無効化します（`WallUtils.DisallowWallJoinAtEnd` のラッパー）。

## 概要
このコマンドは JSON-RPC で Revit MCP アドインに送信され、指定した壁要素について、端部 0 / 1 の「結合を許可しない」フラグを立てます。  
`move_structural_frame` 等と同様に、壁を移動・編集する前に実行することで、Revit 側の結合ダイアログが出るのを抑制する用途を想定しています。

## 使い方
- メソッド: `disallow_wall_join_at_end`

### パラメータ
| 名前       | 型     | 必須                    | 既定値       |
|------------|--------|-------------------------|--------------|
| elementIds | int[]  | いいえ / いずれか一方   | []           |
| elementId  | int    | いいえ / いずれか一方   | 0            |
| uniqueId   | string | いいえ / いずれか一方   |              |
| endIndex   | int    | いいえ                  | （下記参照） |
| ends       | int[]  | いいえ                  | （下記参照） |

- `elementIds` / `elementId` / `uniqueId` のどれか一つは必ず指定してください。
- `endIndex` / `ends`:
  - 有効値は `0` または `1`（始端 / 終端）です。
  - どちらも指定しない場合は、両端 `[0, 1]` が対象になります。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "disallow_wall_join_at_end",
  "params": {
    "elementIds": [12345, 67890],
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
  "notWall": 0,
  "failed": 0
}
```

## メモ
- 対象は Revit の `Wall` 要素のみで、他カテゴリの要素 ID は `notWall` にカウントされます。
- 幾何学的な JoinGeometry の解除は行いません。必要に応じて:
  1. `get_joined_elements` で結合相手を調べる
  2. `unjoin_elements` で JoinGeometry を解除する
  3. `disallow_wall_join_at_end` で端部結合を禁止する  
  といった手順で組み合わせてください。
- その後、壁の移動や編集を行うと、Revit UI の結合ダイアログが出にくくなります。

## 関連コマンド
- set_wall_join_type
- set_wall_miter_joint
- get_joined_elements
- unjoin_elements
