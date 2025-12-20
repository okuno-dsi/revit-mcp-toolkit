# get_parameter_identity

- カテゴリ: ParamOps
- 目的: 指定要素（インスタンス/タイプ）のパラメータを解決し、識別情報とメタデータ（由来、グループ、配置、単位、GUID 等）を詳細に返します。

## 使い方
- メソッド: `get_parameter_identity`

### パラメータ
- `target`（必須）: `{ "by":"elementId|typeId|uniqueId", "value": <id|string> }`
- 次のいずれか（優先順）: `builtInId` → `builtInName` → `guid` → `paramName`
  - エイリアス対応: `name`, `builtIn`, `built_in`, `builtInParameter`, `paramGuid`, `GUID`
- `attachedToOverride`（任意）: `"instance" | "type"` — 先に解決を試す対象を指定。見つからない場合は自動で反対側も確認します。
- `fields`（任意）: 返却の `parameter` ブロックから欲しいキーのみを投影（例: `["name","origin","group","placement","guid"]`）。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_parameter_identity",
  "params": {
    "target": { "by": "elementId", "value": 123456 },
    "builtInName": "ALL_MODEL_TYPE_IMAGE",
    "attachedToOverride": "type",
    "fields": ["name","origin","group","placement","attachedTo","guid","isReadOnly","displayUnit"]
  }
}
```

### 返却例（形）
```jsonc
{
  "ok": true,
  "result": {
    "found": true,
    "target": { "kind": "element|type", "id": 123456, "uniqueId": "..." },
    "resolvedBy": "builtInName:ALL_MODEL_TYPE_IMAGE",
    "parameter": {
      "name": "Type Image",
      "paramId": -1001250,
      "storageType": "ElementId",
      "origin": "builtIn | project | shared | family",
      "group": { "enumName": "PG_IDENTITY_DATA", "uiLabel": "識別情報" },
      "placement": "instance | type",
      "attachedTo": "instance | type",
      "isReadOnly": false,
      "isShared": false,
      "isBuiltIn": true,
      "guid": null,
      "parameterElementId": 0,
      "categories": ["Doors","Windows"],
      "dataType": { "storage": "ElementId", "spec": "Autodesk.Spec:..." },
      "displayUnit": "mm|m2|m3|deg|raw",
      "allowVaryBetweenGroups": null,
      // value ブロックは unitsMode に従います（既定: SI）
      // - SI/Project/Raw: { display, unit, value, raw }
      // - Both          : { display, unitSi, valueSi, unitProject, valueProject, raw }
      "value": { "display": "2000 mm", "unit": "mm", "value": 2000.0, "raw": 6.56168 },
      "notes": "タイプ側の可能性が高いため、タイプ変更で編集"
    }
  }
}
```

注意
- インスタンスで見つからない場合は自動でタイプ要素も確認し、`attachedTo` に実際の所在を示します。
- `origin` は `paramId<0` なら `builtIn`、それ以外で `ExternalDefinition/GUID` があれば `shared`、それ以外は `project` と推定します（ファミリ文書では `family`）。
- `group.uiLabel` は Revit のローカライズ文字列（`LabelUtils`）。
- `fields` は `parameter` のキー投影です（`found/target/resolvedBy` は常に返却）。

## 関連コマンド
- get_param_meta
- get_type_parameters_bulk
- get_instance_parameters_bulk
- update_parameters_batch
## タイムアウトとチャンク化
- 多数のパラメータ識別情報を出力する場合は、先にパラメータIDの一覧を収集し、10～25件程度の塊で分割要求してください。
- 一般的なプロジェクトでは、各呼び出しを10秒未満に抑え、クライアント側タイムアウトを回避できます。必要に応じてチャンク間に短い待機を挟みます。
- GUID ポリシー: GUID は共有パラメータのみ出力します。組込／プロジェクト／ファミリパラメータでは `guid` は常に `null` です。
