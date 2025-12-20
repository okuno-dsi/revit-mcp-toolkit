# create_family_on_face

- カテゴリ: ElementOps
- 目的: このコマンドは『create_family_on_face』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_family_on_face

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| elementId | int | いいえ/状況による |  |
| faceIndex | int | いいえ/状況による |  |
| familySymbolId | int | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_family_on_face",
  "params": {
    "elementId": 0,
    "faceIndex": 0,
    "familySymbolId": 0
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