# create_view_plan

- カテゴリ: ViewOps
- 目的: 平面ビュー（FloorPlan / CeilingPlan など）を作成する

## 概要
指定レベルに平面ビューを作成します。

- メソッド: `create_view_plan`

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| levelName | string | はい |  |
| name | string | いいえ | New Plan |
| viewFamily | string | いいえ | FloorPlan |
| view_family | string | いいえ（互換） |  |

補足:
- `viewFamily` は `FloorPlan`（既定）と `CeilingPlan`（RCP/天井伏図）をサポートします。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_view_plan",
  "params": {
    "levelName": "1FL",
    "name": "1FL Plan",
    "viewFamily": "FloorPlan"
  }
}
```

## 関連
- get_view_info
- get_views
- create_section
- create_elevation_view
