# flip_family_instance_orientation

- カテゴリ: ElementOps
- 目的: このコマンドは『flip_family_instance_orientation』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: flip_family_instance_orientation

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| dryRun | bool | いいえ/状況による | false |
| rotateDeg | number | いいえ/状況による | 0.0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "flip_family_instance_orientation",
  "params": {
    "dryRun": false,
    "rotateDeg": 0.0
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