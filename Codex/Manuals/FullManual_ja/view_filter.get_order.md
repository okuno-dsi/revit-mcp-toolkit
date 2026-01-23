# view_filter.get_order

- カテゴリ: ViewFilterOps
- 目的: ビュー/テンプレート上のフィルタ優先順位（順序）を取得します。

## 概要
`View.GetOrderedFilters()` により、実際の優先順位順（UIの上→下）を返します。

## 使い方
- メソッド: view_filter.get_order

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| view | object | はい |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "view_filter.get_order",
  "params": {
    "view": { "elementId": 12345 }
  }
}
```

## 戻り値
- `orderedFilterIds[]`: 優先順位順のフィルタID

## 関連コマンド
- view_filter.set_order
- view_filter.apply_to_view
- view_filter.remove_from_view

