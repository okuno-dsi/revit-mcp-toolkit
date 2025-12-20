# export_dwg_by_param_groups

- カテゴリ: Export
- 目的: このコマンドは『export_dwg_by_param_groups』を書き出しします。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: export_dwg_by_param_groups

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| asyncMode | bool | いいえ/状況による | true |
| maxMillisPerPass | int | いいえ/状況による | 20000 |
| sessionKey | string | いいえ/状況による | OST_Walls |
| startIndex | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "export_dwg_by_param_groups",
  "params": {
    "asyncMode": false,
    "maxMillisPerPass": 0,
    "sessionKey": "...",
    "startIndex": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- export_schedules_html
- export_dwg
- export_dwg_with_workset_bucketing
- export_view_mesh
- export_view_3dm
- export_view_3dm_brep
- 