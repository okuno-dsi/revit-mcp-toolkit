# move_rebars

- カテゴリ: Rebar
- 目的: 要素IDを指定して鉄筋（Rebar系）を移動します。エイリアス: `move_rebar`。

## 概要
- 任意の elementId を受け取りますが、移動対象は “Rebar系” の要素に限定します:
  - `Rebar`, `RebarInSystem`, `AreaReinforcement`, `PathReinforcement`
- オフセット指定は以下に対応します（mm）:
  - `offsetMm: {x,y,z}`（推奨）
  - `dx/dy/dz`
- `items[]` で「要素ごとに異なるオフセット」も指定できます。
- `batchSize` によりトランザクションを分割できます。

## 使い方
- Method: `move_rebars`（alias: `move_rebar`）

### パラメータ（共通オフセット）
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---:|---:|---|
| rebarElementIds | int[] | no |  | 対象ID（推奨）。 |
| elementIds | int[] | no |  | 互換入力（エイリアス）。 |
| useSelectionIfEmpty | bool | no | true | IDが空なら選択を使う。 |
| offsetMm | object | yes/depends |  | `{x,y,z}`（mm）。 |
| dx/dy/dz | number | yes/depends |  | 代替指定（mm）。 |
| dryRun | bool | no | false | true の場合は移動せず対象を返す。 |
| batchSize | int | no | 200 | トランザクション分割の単位。 |

### パラメータ（要素ごと）
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---:|---:|---|
| items | object[] | yes/depends |  | `elementId`（または `rebarElementId`）＋オフセット。 |

### リクエスト例（共通オフセット）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "move_rebars",
  "params": {
    "rebarElementIds": [123456],
    "offsetMm": { "x": 100, "y": 0, "z": 0 }
  }
}
```

## 関連
- `delete_rebars`
- `apply_transform_delta`（単体要素の平行移動/回転）

