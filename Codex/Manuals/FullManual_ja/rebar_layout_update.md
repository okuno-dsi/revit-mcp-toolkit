# rebar_layout_update

- カテゴリ: Rebar
- 目的: 指定した鉄筋（Rebar）の配筋（レイアウト）設定を更新します（shape-driven のセットのみ対応）。

## 使い方
- Method: `rebar_layout_update`（alias: `rebar_arrangement_update`）

### パラメータ
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---:|---:|---|
| rebarElementIds | int[] | yes |  | 対象の鉄筋要素ID。 |
| elementIds | int[] | no |  | 互換入力（エイリアス）。 |
| useSelectionIfEmpty | bool | no | false | `rebarElementIds` が空の場合に選択要素から取得するか。 |
| layout | object | yes |  | レイアウト指定（下記参照）。 |

### layout の形式
```json
{
  "rule": "maximum_spacing",
  "spacingMm": 150.0,
  "arrayLengthMm": 2900.0,
  "numberOfBarPositions": null,
  "barsOnNormalSide": true,
  "includeFirstBar": true,
  "includeLastBar": true
}
```

`rule` の値:
- `single`
- `fixed_number`
- `maximum_spacing`
- `number_with_spacing`
- `minimum_clear_spacing`（API対応時のみ：リフレクションでベストエフォート）

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "rebar_layout_update",
  "params": {
    "rebarElementIds": [1234567],
    "layout": {
      "rule": "maximum_spacing",
      "spacingMm": 150.0,
      "arrayLengthMm": 2900.0,
      "barsOnNormalSide": true,
      "includeFirstBar": true,
      "includeLastBar": true
    }
  }
}
```

## 注意
- free-form 鉄筋は `NOT_SHAPE_DRIVEN` としてスキップされます。
- `updatedRebarIds[]` と `skipped[]` を返します。

## 関連
- `rebar_layout_inspect`
- `rebar_layout_update_by_host`
