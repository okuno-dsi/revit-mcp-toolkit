# get_joined_elements

- カテゴリ: ElementOps
- 目的: 指定した 1 つの要素について、幾何学的な「結合」だけでなく、ホスト / グループ / ピン / 従属要素などの拘束状態を調べます。

## 概要
このコマンドは JSON-RPC で Revit MCP アドインに送信されます。対象要素について、次の情報を返します。

- JoinGeometryUtils による幾何学的な結合先 (`joinedIds`)
- ホスト / ファミリ関係（`hostId`, `superComponentId`, `subComponentIds`）
- ピン状態 (`isPinned`) とグループ所属 (`isInGroup`, `groupId`)
- 寸法・タグなどの従属要素 (`dependentIds`)
- 安全に自動化しやすい後続コマンド候補（`suggestedCommands`）
- UI で慎重に行うべき操作に関するコメント（`notes`）

要素の移動や形状変更を行う前に、どのような拘束が掛かっているかを確認する用途を想定しています。

## 使い方
- メソッド: get_joined_elements

### パラメータ
| 名前       | 型     | 必須         | 既定値 |
|------------|--------|--------------|--------|
| elementId  | int    | いいえ / いずれか一方 | 0      |
| uniqueId   | string | いいえ / いずれか一方 |        |

`elementId` と `uniqueId` のどちらか一方は必ず指定してください。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_joined_elements",
  "params": {
    "elementId": 39399
  }
}
```

### 結果の例（形だけ）
```json
{
  "ok": true,
  "elementId": 39399,
  "joinedIds": [11111, 22222],
  "hostId": null,
  "superComponentId": null,
  "subComponentIds": [],
  "isPinned": false,
  "isInGroup": false,
  "groupId": null,
  "dependentIds": [39399, 39400],
  "suggestedCommands": [
    { "kind": "geometryJoin", "command": "unjoin_elements", "description": "結合要素との Join を解除します。" },
    { "kind": "pin", "command": "unpin_element", "description": "ピン留めを解除して移動できる状態にします。" }
  ],
  "notes": [
    "ホスト / グループ / 寸法・タグの編集や削除は、基本的に Revit UI 上で慎重に行うことを推奨します。"
  ]
}
```

## 関連コマンド
- unjoin_elements
- unpin_element
- unpin_elements
