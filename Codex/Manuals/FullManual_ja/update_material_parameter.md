# update_material_parameter

- カテゴリ: ElementOps
- 目的: このコマンドは『update_material_parameter』を更新します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: update_material_parameter

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| materialId | int | いいえ/状況による | 0 |
| paramName | string | いいえ/状況による |  |
| uniqueId | string | いいえ/状況による |  |
| value | unknown | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_material_parameter",
  "params": {
    "materialId": 0,
    "paramName": "...",
    "uniqueId": "...",
    "value": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- get_material_parameters
- list_material_parameters
- duplicate_material
- rename_material
- delete_material
- create_material
- apply_material_to_element
- 