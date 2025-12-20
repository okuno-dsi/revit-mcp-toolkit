# create_area_plan

- カテゴリ: Area
- 目的: このコマンドは『create_area_plan』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_area_plan

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| areaSchemeId | unknown | いいえ/状況による |  |
| levelId | unknown | いいえ/状況による |  |
| name | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_area_plan",
  "params": {
    "areaSchemeId": "...",
    "levelId": "...",
    "name": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- create_area
- get_areas
- get_area_params
- update_area
- move_area
- delete_area
- get_area_boundary_walls
- 