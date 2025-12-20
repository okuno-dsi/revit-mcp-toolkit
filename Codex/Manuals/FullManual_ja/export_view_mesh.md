# export_view_mesh

- カテゴリ: Export
- 目的: このコマンドは『export_view_mesh』を書き出しします。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: export_view_mesh

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| detailLevel | string | いいえ/状況による |  |
| includeLinked | bool | いいえ/状況による | true |
| unitsOut | string | いいえ/状況による | feet |
| viewId | int | いいえ/状況による | 0 |
| weld | bool | いいえ/状況による | true |
| weldTolerance | number | いいえ/状況による | 1 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "export_view_mesh",
  "params": {
    "detailLevel": "...",
    "includeLinked": false,
    "unitsOut": "...",
    "viewId": 0,
    "weld": false,
    "weldTolerance": 0.0
  }
}
```

## 関連コマンド
## 関連コマンド
- export_schedules_html
- export_dwg
- export_dwg_with_workset_bucketing
- export_dwg_by_param_groups
- export_view_3dm
- export_view_3dm_brep
- 