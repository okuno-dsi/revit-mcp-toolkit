# create_structural_frame

- カテゴリ: ElementOps
- 目的: このコマンドは『create_structural_frame』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_structural_frame

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| end | object | いいえ/状況による |  |
| familyName | string | いいえ/状況による |  |
| levelId | unknown | いいえ/状況による |  |
| levelName | unknown | いいえ/状況による |  |
| start | object | いいえ/状況による |  |
| typeId | unknown | いいえ/状況による |  |
| typeName | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_structural_frame",
  "params": {
    "end": "...",
    "familyName": "...",
    "levelId": "...",
    "levelName": "...",
    "start": "...",
    "typeId": "...",
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