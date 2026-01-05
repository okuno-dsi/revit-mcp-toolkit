# rebar_layout_inspect

- カテゴリ: Rebar
- 目的: 選択した鉄筋（Rebar）の配筋（レイアウト）設定を取得します（shape-driven のセットのみ対象）。

## 使い方
- Method: `rebar_layout_inspect`

### パラメータ
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---:|---:|---|
| rebarElementIds | int[] | no |  | 取得対象の鉄筋要素ID。 |
| elementIds | int[] | no |  | 互換入力（エイリアス）。 |
| useSelectionIfEmpty | bool | no | true | `rebarElementIds` が空の場合、選択要素から取得します。 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "rebar_layout_inspect",
  "params": {
    "useSelectionIfEmpty": true
  }
}
```

## 戻り値メモ
- `rebars[]` に、IDごとの情報を返します:
  - `layoutRule`（Revit APIの列挙値文字列）
  - `includeFirstBar`, `includeLastBar`, `numberOfBarPositions`
  - `maxSpacingMm`（取得できる場合）
  - `isShapeDriven`（free-form や非Rebarの場合は false）
  - `barsOnNormalSide`, `arrayLengthMm`, `spacingMm`（shape-driven の場合）

## 関連
- `rebar_layout_update`
- `rebar_layout_update_by_host`
- `rebar_mapping_resolve`
