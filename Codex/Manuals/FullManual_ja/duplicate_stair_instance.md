# duplicate_stair_instance

- カテゴリ: ElementOps
- 目的: このコマンドは『duplicate_stair_instance』を複製します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: duplicate_stair_instance

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| elementId | int | いいえ/状況による | 0 |
| offset | object | いいえ/状況による |  |
| uniqueId | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "duplicate_stair_instance",
  "params": {
    "elementId": 0,
    "offset": "...",
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