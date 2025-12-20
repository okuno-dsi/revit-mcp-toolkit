# set_project_base_point

- カテゴリ: SiteOps
- 目的: このコマンドは『set_project_base_point』を設定します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: set_project_base_point

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| angleToTrueNorthDeg | number | いいえ/状況による | 0.0 |
| sharedSiteName | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_project_base_point",
  "params": {
    "angleToTrueNorthDeg": 0.0,
    "sharedSiteName": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- append_toposurface_points
- replace_toposurface_points
- place_building_pad
- create_property_line_from_points
- set_survey_point
- set_shared_coordinates_from_points
- get_site_overview
- 