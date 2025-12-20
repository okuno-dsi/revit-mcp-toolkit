# get_topography_info

- カテゴリ: SiteOps
- 目的: このコマンドは『get_topography_info』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_topography_info

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| materialName | string | いいえ/状況による |  |
| subregionId | int | いいえ/状況による |  |
| topographyId | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_topography_info",
  "params": {
    "materialName": "...",
    "subregionId": 0,
    "topographyId": 0
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