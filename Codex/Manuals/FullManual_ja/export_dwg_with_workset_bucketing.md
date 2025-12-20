# export_dwg_with_workset_bucketing

- カテゴリ: Export
- 目的: このコマンドは『export_dwg_with_workset_bucketing』を書き出しします。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: export_dwg_with_workset_bucketing

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| dwgVersion | string | いいえ/状況による |  |
| fileName | string | いいえ/状況による |  |
| keepTempView | bool | いいえ/状況による | false |
| outputFolder | string | いいえ/状況による |  |
| unmatchedWorksetName | string | いいえ/状況による | WS_UNMATCHED |
| useExportSetup | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "export_dwg_with_workset_bucketing",
  "params": {
    "dwgVersion": "...",
    "fileName": "...",
    "keepTempView": false,
    "outputFolder": "...",
    "unmatchedWorksetName": "...",
    "useExportSetup": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- export_schedules_html
- export_dwg
- export_dwg_by_param_groups
- export_view_mesh
- export_view_3dm
- export_view_3dm_brep
- 