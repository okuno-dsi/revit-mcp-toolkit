# get_instance_geometry

- カテゴリ: ElementOps
- 目的: このコマンドは『get_instance_geometry』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_instance_geometry

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| detailLevel | string | いいえ/状況による |  |
| elementId | unknown | いいえ/状況による |  |
| includeNonVisible | bool | いいえ/状況による | false |
| uniqueId | unknown | いいえ/状況による |  |
| weld | bool | いいえ/状況による | true |
| weldTolerance | number | いいえ/状況による | 1 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_instance_geometry",
  "params": {
    "detailLevel": "...",
    "elementId": "...",
    "includeNonVisible": false,
    "uniqueId": "...",
    "weld": false,
    "weldTolerance": 0.0
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