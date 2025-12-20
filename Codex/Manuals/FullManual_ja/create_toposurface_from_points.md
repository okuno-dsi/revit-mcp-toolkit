# create_toposurface_from_points

- カテゴリ: SiteOps
- 目的: このコマンドは『create_toposurface_from_points』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_toposurface_from_points

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| siteName | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_toposurface_from_points",
  "params": {
    "siteName": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- replace_toposurface_points
- place_building_pad
- create_property_line_from_points
- set_project_base_point
- set_survey_point
- set_shared_coordinates_from_points
- get_site_overview
- 