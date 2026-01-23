# view_filter.apply_to_view

- カテゴリ: ViewFilterOps
- 目的: ビュー/テンプレートへフィルタを適用し、表示設定を反映します。

## 概要
ビュー（またはビュー テンプレート）にフィルタを適用し、必要に応じて:
- 有効/無効
- 表示/非表示
- グラフィックオーバーライド（ハーフトーン/透過/線色・太さ 等の一部）
を設定します。

ビューにビューテンプレートが適用されている場合、V/G（Filters）がロックされることがあります。
その場合は `detachViewTemplate=true` を指定するか、テンプレートビューを対象にしてください。

## 使い方
- メソッド: view_filter.apply_to_view

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| application | object | はい |  |
| detachViewTemplate | bool | いいえ | false |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "view_filter.apply_to_view",
  "params": {
    "detachViewTemplate": false,
    "application": {
      "view": { "elementId": 12345 },
      "filter": { "name": "A-WALLS-RC" },
      "enabled": true,
      "visible": true,
      "overrides": {
        "transparency": 0,
        "projectionLine": { "color": "#FF0000", "weight": 5 }
      }
    }
  }
}
```

## 関連コマンド
- view_filter.remove_from_view
- view_filter.get_order
- view_filter.set_order

