# update_structural_foundation_geometry

- カテゴリ: ElementOps
- 目的: このコマンドは『update_structural_foundation_geometry』を更新します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: update_structural_foundation_geometry

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| elementId | unknown | いいえ/状況による |  |
| offset | unknown | いいえ/状況による |  |
| rotation | unknown | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_structural_foundation_geometry",
  "params": {
    "elementId": "...",
    "offset": "...",
    "rotation": "..."
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