# change_structural_frame_type

- カテゴリ: ElementOps
- 目的: このコマンドは『change_structural_frame_type』を変更します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: change_structural_frame_type

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| elementId | int | いいえ/状況による | 0 |
| familyName | string | いいえ/状況による |  |
| typeId | int | いいえ/状況による | 0 |
| typeName | string | いいえ/状況による |  |
| uniqueId | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "change_structural_frame_type",
  "params": {
    "elementId": 0,
    "familyName": "...",
    "typeId": 0,
    "typeName": "...",
    "uniqueId": "..."
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