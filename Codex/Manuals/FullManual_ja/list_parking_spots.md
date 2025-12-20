# list_parking_spots

- カテゴリ: SiteOps
- 目的: このコマンドは『list_parking_spots』を一覧取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: list_parking_spots

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| angleDeg | number | いいえ/状況による | 0.0 |
| elementId | unknown | いいえ/状況による |  |
| familyName | string | いいえ/状況による |  |
| ids | unknown | いいえ/状況による |  |
| levelName | string | いいえ/状況による |  |
| typeName | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "list_parking_spots",
  "params": {
    "angleDeg": 0.0,
    "elementId": "...",
    "familyName": "...",
    "ids": "...",
    "levelName": "...",
    "typeName": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- append_toposurface_points
- replace_toposurface_points
- place_building_pad
- create_property_line_from_points
- set_project_base_point
- set_survey_point
- set_shared_coordinates_from_points
- 