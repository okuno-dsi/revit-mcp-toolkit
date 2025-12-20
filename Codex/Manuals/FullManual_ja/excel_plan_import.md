# excel_plan_import

- カテゴリ: ExcelPlan
- 目的: このコマンドは『excel_plan_import』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: excel_plan_import

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| baseOffsetMm | number | いいえ/状況による | 0.0 |
| cellSizeMeters | number | いいえ/状況による | 1.0 |
| debugSheetName | string | いいえ/状況による | Recreated |
| debugWriteBack | bool | いいえ/状況による | false |
| ensurePerimeter | bool | いいえ/状況による | false |
| excelPath | string | いいえ/状況による |  |
| exportOnly | bool | いいえ/状況による | false |
| flip | bool | いいえ/状況による | false |
| heightMm | number | いいえ/状況による |  |
| levelName | string | いいえ/状況による |  |
| mode | string | いいえ/状況による | Walls |
| placeGrids | bool | いいえ/状況による | false |
| placeRooms | bool | いいえ/状況による | false |
| roomLevelName | string | いいえ/状況による |  |
| roomPhaseName | string | いいえ/状況による |  |
| serviceUrl | string | いいえ/状況による |  |
| setRoomNameFromLabel | bool | いいえ/状況による | true |
| sheetName | string | いいえ/状況による |  |
| toleranceCell | number | いいえ/状況による | 0.001 |
| topLevelName | string | いいえ/状況による |  |
| useColorMask | bool | いいえ/状況による | true |
| wallTypeName | string | いいえ/状況による | RC150 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "excel_plan_import",
  "params": {
    "baseOffsetMm": 0.0,
    "cellSizeMeters": 0.0,
    "debugSheetName": "...",
    "debugWriteBack": false,
    "ensurePerimeter": false,
    "excelPath": "...",
    "exportOnly": false,
    "flip": false,
    "heightMm": 0.0,
    "levelName": "...",
    "mode": "...",
    "placeGrids": false,
    "placeRooms": false,
    "roomLevelName": "...",
    "roomPhaseName": "...",
    "serviceUrl": "...",
    "setRoomNameFromLabel": false,
    "sheetName": "...",
    "toleranceCell": 0.0,
    "topLevelName": "...",
    "useColorMask": false,
    "wallTypeName": "..."
  }
}
```