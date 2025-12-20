# lookup_element

- カテゴリ: Misc
- 目的: 単一の要素について、RevitLookup に近い情報（属性・位置・パラメータ・簡易ジオメトリ・関係）を JSON 形式で取得します。

## 概要
このコマンドは JSON-RPC で Revit MCP アドインに送信され、指定した要素を RevitLookup のように「丸ごと」調査した結果を JSON で返します。  
読み取り専用のコマンドであり、他の編集系コマンドから安全に参照できる“インスペクション用”のプリミティブです。

## 使い方
- メソッド: `lookup_element`

### パラメータ
| 名前             | 型     | 必須               | 既定値 |
|------------------|--------|--------------------|--------|
| elementId        | int    | いいえ / いずれか | 0      |
| uniqueId         | string | いいえ / いずれか |        |
| includeGeometry  | bool   | いいえ            | true   |
| includeRelations | bool   | いいえ            | true   |

- `elementId` と `uniqueId` のどちらか一方は必ず指定してください。
- 両方指定された場合は、`uniqueId` が優先されます。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": "1",
  "method": "lookup_element",
  "params": {
    "elementId": 6798116,
    "includeGeometry": true,
    "includeRelations": true
  }
}
```

### 結果の例（形だけ）
```json
{
  "ok": true,
  "element": {
    "id": 6798116,
    "uniqueId": "8c0a...-0067bb24",
    "category": "壁",
    "categoryId": -2000011,
    "familyName": "外壁",
    "typeName": "(某)W3",
    "isElementType": false,
    "level": { "id": 1147622, "name": "3FL" },
    "workset": { "id": 0, "name": "ワークセット1" },
    "designOption": { "id": null, "name": null },
    "location": {
      "kind": "LocationCurve",
      "curveType": "Line",
      "start": { "x": 87.59, "y": 64.42, "z": 26.90 },
      "end":   { "x": 92.52, "y": 64.42, "z": 26.90 }
    },
    "boundingBox": {
      "min": { "x": 87.40, "y": 64.22, "z": 26.90 },
      "max": { "x": 92.52, "y": 64.62, "z": 39.53 }
    },
    "geometrySummary": {
      "hasSolid": true,
      "solidCount": 1,
      "approxVolume": 0.686,
      "approxSurfaceArea": 12.715
    },
    "parameters": [
      {
        "name": "タイプ名",
        "builtin": "SYMBOL_NAME_PARAM",
        "storageType": "String",
        "parameterGroup": "PG_IDENTITY_DATA",
        "parameterGroupLabel": "識別情報",
        "isReadOnly": true,
        "isShared": false,
        "isInstance": false,
        "guid": null,
        "value": "RC150",
        "displayValue": "RC150"
      }
      // 他のパラメータ...
    ],
    "relations": {
      "hostId": null,
      "superComponentId": null,
      "groupId": null
    }
  }
}
```

## メモ
- ジオメトリ要約は簡易版で、ソリッドの個数・体積・表面積（概算）のみを返します。詳細なメッシュや全てのフェース情報は含まれません。
- `parameters` にはインスタンス／タイプ、プロジェクト／共有パラメータを問わず、取得可能なものがすべて含まれます。
- `relations` には現状、ホスト・親コンポーネント・グループ所属などの基本的な関係だけが含まれます。必要に応じて将来的に拡張される可能性があります。

## 関連コマンド
- get_element_info
- get_selected_element_ids
