# export_view_3dm_brep

- カテゴリ: Export
- 目的: このコマンドは『export_view_3dm_brep』を書き出しします。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: export_view_3dm_brep

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| angTolDeg | number | いいえ/状況による | 1.0 |
| curveTol | number | いいえ/状況による | 1 |
| includeLinked | bool | いいえ/状況による | true |
| layerMode | string | いいえ/状況による | byCategory |
| outPath | string | いいえ/状況による |  |
| unitsOut | string | いいえ/状況による | mm |
| viewId | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "export_view_3dm_brep",
  "params": {
    "angTolDeg": 0.0,
    "curveTol": 0.0,
    "includeLinked": false,
    "layerMode": "...",
    "outPath": "...",
    "unitsOut": "...",
    "viewId": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- export_schedules_html
- export_dwg
- export_dwg_with_workset_bucketing
- export_dwg_by_param_groups
- export_view_mesh
- export_view_3dm
- 