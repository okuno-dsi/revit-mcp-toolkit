# get_area_boundary_duplicates

- カテゴリ: Area
- 目的: このコマンドは『get_area_boundary_duplicates』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_area_boundary_duplicates

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| mergeToleranceMm | number | いいえ/状況による | 5.0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_area_boundary_duplicates",
  "params": {
    "mergeToleranceMm": 0.0
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