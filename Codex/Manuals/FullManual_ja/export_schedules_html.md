# export_schedules_html

- カテゴリ: Export
- 目的: このコマンドは『export_schedules_html』を書き出しします。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: export_schedules_html

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| maxRows | int | いいえ/状況による |  |
| outDir | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "export_schedules_html",
  "params": {
    "maxRows": 0,
    "outDir": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- export_dwg
- export_dwg_with_workset_bucketing
- export_dwg_by_param_groups
- export_view_mesh
- export_view_3dm
- export_view_3dm_brep
- 