# swap_roof_layer_materials

- カテゴリ: ElementOps
- 目的: このコマンドは『swap_roof_layer_materials』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: swap_roof_layer_materials

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| duplicateIfInUse | unknown | いいえ/状況による |  |
| find | unknown | いいえ/状況による |  |
| materialId | unknown | いいえ/状況による |  |
| materialName | unknown | いいえ/状況による |  |
| materialNameContains | unknown | いいえ/状況による |  |
| maxCount | unknown | いいえ/状況による |  |
| replace | unknown | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "swap_roof_layer_materials",
  "params": {
    "duplicateIfInUse": "...",
    "find": "...",
    "materialId": "...",
    "materialName": "...",
    "materialNameContains": "...",
    "maxCount": "...",
    "replace": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- get_material_parameters
- list_material_parameters
- update_material_parameter
- duplicate_material
- rename_material
- delete_material
- create_material
- 