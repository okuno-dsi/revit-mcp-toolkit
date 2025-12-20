# create_curtain_wall

- カテゴリ: ElementOps
- 目的: このコマンドは『create_curtain_wall』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_curtain_wall

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| baseLevelId | unknown | いいえ/状況による |  |
| baseLevelName | unknown | いいえ/状況による |  |
| baseOffsetMm | number | いいえ/状況による | 0.0 |
| levelId | unknown | いいえ/状況による |  |
| levelName | unknown | いいえ/状況による |  |
| topLevelId | unknown | いいえ/状況による |  |
| topLevelName | unknown | いいえ/状況による |  |
| topOffsetMm | number | いいえ/状況による | 0.0 |
| typeId | unknown | いいえ/状況による |  |
| typeName | unknown | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_curtain_wall",
  "params": {
    "baseLevelId": "...",
    "baseLevelName": "...",
    "baseOffsetMm": 0.0,
    "levelId": "...",
    "levelName": "...",
    "topLevelId": "...",
    "topLevelName": "...",
    "topOffsetMm": 0.0,
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