# export_dwg

- カテゴリ: Export
- 目的: このコマンドは『export_dwg』を書き出しします。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: export_dwg

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| batchSize | int | いいえ/状況による | 10 |
| dwgVersion | string | いいえ/状況による |  |
| fileName | string | いいえ/状況による |  |
| keepTempView | bool | いいえ/状況による | false |
| maxMillisPerTx | int | いいえ/状況による | 0 |
| outputFolder | string | いいえ/状況による |  |
| startIndex | int | いいえ/状況による | 0 |
| useExportSetup | string | いいえ/状況による |  |
| viewId | int | いいえ/状況による |  |
| viewUniqueId | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "export_dwg",
  "params": {
    "batchSize": 0,
    "dwgVersion": "...",
    "fileName": "...",
    "keepTempView": false,
    "maxMillisPerTx": 0,
    "outputFolder": "...",
    "startIndex": 0,
    "useExportSetup": "...",
    "viewId": 0,
    "viewUniqueId": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- export_schedules_html
- export_dwg_with_workset_bucketing
- export_dwg_by_param_groups
- export_view_mesh
- export_view_3dm
- export_view_3dm_brep
- 