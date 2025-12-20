# place_building_pad

- カテゴリ: SiteOps
- 目的: このコマンドは『place_building_pad』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: place_building_pad

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| levelName | string | いいえ/状況による |  |
| offsetMm | number | いいえ/状況による | 0.0 |
| padTypeName | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "place_building_pad",
  "params": {
    "levelName": "...",
    "offsetMm": 0.0,
    "padTypeName": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- append_toposurface_points
- replace_toposurface_points
- create_property_line_from_points
- set_project_base_point
- set_survey_point
- set_shared_coordinates_from_points
- get_site_overview
- 