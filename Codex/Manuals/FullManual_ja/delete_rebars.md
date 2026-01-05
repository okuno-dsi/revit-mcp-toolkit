# delete_rebars

- カテゴリ: Rebar
- 目的: 要素IDを指定して鉄筋（Rebar系）を削除します。エイリアス: `delete_rebar`。

## 概要
- 任意の elementId を受け取りますが、削除対象は “Rebar系” の要素に限定します:
  - `Rebar`, `RebarInSystem`, `AreaReinforcement`, `PathReinforcement`
- `dryRun` と、長いトランザクション回避のための分割実行（`batchSize`）に対応します。
- 高リスク操作なので、必要に応じてルータの confirm-token フロー（`requireConfirmToken/confirmToken`）と併用してください。

## 使い方
- Method: `delete_rebars`（alias: `delete_rebar`）

### パラメータ
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---:|---:|---|
| rebarElementIds | int[] | no |  | 対象ID（推奨）。 |
| elementIds | int[] | no |  | 互換入力（エイリアス）。 |
| rebarElementId / elementId | int | no |  | 単体指定。 |
| useSelectionIfEmpty | bool | no | true | IDが空なら選択を使う。 |
| dryRun | bool | no | false | true の場合は削除せず対象を返す。 |
| batchSize | int | no | 200 | トランザクション分割の単位。 |
| maxIds | int | no | 2000 | 返却ID配列を省略する上限。 |
| detailLimit | int | no | 200 | dryRun時に detail を最大N件返す。 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "delete_rebars",
  "params": {
    "rebarElementIds": [123456, 123457],
    "dryRun": true
  }
}
```

## 関連
- `move_rebars`
- `rebar_regenerate_delete_recreate`（タグ付きのみの delete&recreate）

