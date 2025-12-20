# change_roof_type

- カテゴリ: ElementOps
- 目的: このコマンドは『change_roof_type』を変更します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: change_roof_type

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| familyName | string | いいえ/状況による |  |
| typeName | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "change_roof_type",
  "params": {
    "familyName": "...",
    "typeName": "..."
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