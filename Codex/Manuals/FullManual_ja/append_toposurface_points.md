# append_toposurface_points

- カテゴリ: SiteOps
- 目的: このコマンドは『append_toposurface_points』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: append_toposurface_points

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| topoId | unknown | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "append_toposurface_points",
  "params": {
    "topoId": "..."
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