# rebar_layout_update_by_host

- カテゴリ: Rebar
- 目的: ホスト要素（柱/梁など）に含まれる鉄筋セットを収集し、一括でレイアウト更新します。

## 使い方
- Method: `rebar_layout_update_by_host`

### パラメータ
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---:|---:|---|
| hostElementIds | int[] | no |  | ホスト要素ID（RebarHostDataで有効なホストである必要があります）。 |
| useSelectionIfEmpty | bool | no | true | `hostElementIds` が空の場合、選択要素から取得します。 |
| layout | object | yes |  | `rebar_layout_update` と同じ layout 指定。 |
| filter | object | no |  | 任意フィルタ。 |
| filter.commentsTagEquals | string | no |  | Rebarの`コメント(Comments)`がこの文字列と一致するもののみ更新します（前後空白除去、大小無視）。 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "rebar_layout_update_by_host",
  "params": {
    "useSelectionIfEmpty": true,
    "layout": {
      "rule": "fixed_number",
      "numberOfBarPositions": 10,
      "arrayLengthMm": 2500.0,
      "barsOnNormalSide": true,
      "includeFirstBar": true,
      "includeLastBar": true
    }
  }
}
```

## 注意
- 各ホストは `RebarHostData.IsValidHost` で検証します。
- `updatedRebarIds[]` と `skipped[]` を返します（ホスト側/鉄筋側の理由が混在します）。

## 関連
- `rebar_layout_update`
- `rebar_layout_inspect`

