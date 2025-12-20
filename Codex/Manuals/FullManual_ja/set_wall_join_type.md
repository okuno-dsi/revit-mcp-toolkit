# set_wall_join_type / set_wall_miter_joint

- カテゴリ: ElementOps / Wall
- 目的: 壁要素の端部における結合タイプ（Miter / Butt）を設定します。

## 概要
このコマンドは JSON-RPC で Revit MCP アドインに送信され、本来は `WallUtils.SetWallJoinType` を使って壁どうしの接合方法を変更する想定です。  
しかし、本アドインが対象としている Revit のビルドでは、`WallJoinType` / `WallUtils.SetWallJoinType` がパブリック API として公開されていないため、現状は常に `ok:false`（「MCP からは壁結合タイプを変更できない」というメッセージ）を返すスタブ実装になっています。  
メソッド名 `set_wall_miter_joint` も将来用のエイリアスとして予約されていますが、現時点では同様にエラーを返すだけです。

## 使い方
- メソッド: `set_wall_join_type`  
  またはエイリアス: `set_wall_miter_joint`

### パラメータ
| 名前       | 型      | 必須                    | 既定値           |
|------------|---------|-------------------------|------------------|
| elementIds | int[]   | いいえ / いずれか一方   | []               |
| elementId  | int     | いいえ / いずれか一方   | 0                |
| uniqueId   | string  | いいえ / いずれか一方   |                  |
| joinType   | string  | いいえ（下記参照）      | （エイリアス依存） |
| endIndex   | int     | いいえ                  | （下記参照）     |
| ends       | int[]   | いいえ                  | （下記参照）     |

- `elementIds` / `elementId` / `uniqueId` のどれか一つは必ず指定してください。
- `joinType`:
  - `"Miter"` または `"Butt"`（大文字小文字は不問）。
  - 省略時にメソッド名が `set_wall_miter_joint` の場合は `"Miter"` とみなされます。
- `endIndex` / `ends`:
  - 有効値は `0` または `1`（始端 / 終端）です。
  - どちらも指定しない場合は、両端 `[0, 1]` が対象になります。

### リクエスト例（結合タイプを明示）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_wall_join_type",
  "params": {
    "elementIds": [12345, 67890],
    "joinType": "Miter",
    "ends": [0, 1]
  }
}
```

### リクエスト例（Miter 専用エイリアス）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_wall_miter_joint",
  "params": {
    "elementId": 12345,
    "endIndex": 0
  }
}
```

### 結果の例（API でサポートされる場合のイメージ）
```json
{
  "ok": true,
  "requested": 2,
  "processed": 2,
  "changed": 2,
  "notWall": 0,
  "noJoin": 0,
  "failed": 0,
  "joinType": "Miter"
}
```

## メモ
- 現状の Revit バージョン / ビルドでは、壁の結合タイプ（Miter / Butt）を MCP から変更することはできません。このコマンドはスタブとして `ok:false` と説明メッセージを返すだけです。
- 壁の結合タイプを変更したい場合は、Revit UI 上で直接操作してください。
- MCP 側では、代わりに:
  - `disallow_wall_join_at_end` で結合を許可する端 / 禁止する端を制御し、
  - `get_joined_elements` / `unjoin_elements` でジオメトリ上の Join を管理する  
  といった形での補助が可能です。

## 関連コマンド
- disallow_wall_join_at_end
- get_joined_elements
- unjoin_elements
