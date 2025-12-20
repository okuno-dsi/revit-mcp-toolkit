# create_property_line_from_points

- カテゴリ: SiteOps
- 目的: このコマンドは『create_property_line_from_points』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_property_line_from_points

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| closed | bool | いいえ/状況による | true |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_property_line_from_points",
  "params": {
    "closed": false
  }
}
```

## 関連コマンド
## 関連コマンド
- append_toposurface_points
- replace_toposurface_points
- place_building_pad
- set_project_base_point
- set_survey_point
- set_shared_coordinates_from_points
- get_site_overview
- 