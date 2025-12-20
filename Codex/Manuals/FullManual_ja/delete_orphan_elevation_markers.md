# delete_orphan_elevation_markers

- カテゴリ: ViewOps
- 目的: 孤立した ElevationMarker（立面マーカー）を一括削除します。

## 概要
建具の立面ビュー等を作成すると、Revit は平面図上に `ElevationMarker` 要素（立面マーカー）を生成します。  
その後、立面ビュー（`ViewSection` 等）を削除しても、マーカー要素だけが残り「孤立マーカー」になることがあります。

`delete_orphan_elevation_markers` は、`ElevationMarker` を走査し、**紐づくビューが 0 件（全スロットが空、または削除済みビューを参照）**のものだけを削除します。

## 使い方
- メソッド: delete_orphan_elevation_markers

### パラメータ
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---|---|---|
| dryRun | bool | いいえ | false | `true` の場合、候補の列挙のみ（削除しない）。 |
| viewId | int | いいえ | 0 | 指定したビューに可視なマーカーだけを対象にする。 |
| batchSize | int | いいえ | 200 | 1トランザクションで削除する件数（長いトランザクション回避）。 |
| limit | int | いいえ | 0 | 0より大きい場合、先頭 N 件だけ削除。 |
| detailLimit | int | いいえ | 200 | レスポンスで詳細を返す件数上限。 |
| maxIds | int | いいえ | 2000 | レスポンス内のID配列をトリムする上限。 |

### リクエスト例（DryRun）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "delete_orphan_elevation_markers",
  "params": {
    "dryRun": true
  }
}
```

## 注意事項
- このコマンドは **ビュー自体は削除しません**。ビューが 0 件のマーカーのみ削除対象です。
- まだビューが残っているマーカーを消したい場合は、Revit UI 側でマーカーを削除する（または残存ビューを削除してから本コマンドを実行）してください。

