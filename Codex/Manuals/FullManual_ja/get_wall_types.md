# get_wall_types

- カテゴリ: ElementOps
- 目的: このコマンドは『get_wall_types』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_wall_types

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| count | int | いいえ/状況による |  |
| nameContains | string | いいえ/状況による |  |
| skip | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_wall_types",
  "params": {
    "count": 0,
    "nameContains": "...",
    "skip": 0
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