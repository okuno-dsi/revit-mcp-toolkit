# create_structural_foundation

- カテゴリ: ElementOps
- 目的: このコマンドは『create_structural_foundation』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_structural_foundation

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| levelId | int | いいえ/状況による |  |
| typeId | int | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_structural_foundation",
  "params": {
    "levelId": 0,
    "typeId": 0
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