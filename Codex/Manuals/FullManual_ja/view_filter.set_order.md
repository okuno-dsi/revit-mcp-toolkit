# view_filter.set_order

- カテゴリ: ViewFilterOps
- 目的: フィルタ順序を変更しつつ、設定（表示/有効/オーバーライド）を保持します。

## 概要
フィルタ順序は Revit API 上「直接設定」ができないため、一般的な手法として
一度Remove/Addし直して順序を作り、各フィルタの状態を復元します。

ビューにビューテンプレートが適用されている場合はロックされることがあるため、
必要なら `detachViewTemplate=true` を指定してください。

## 使い方
- メソッド: view_filter.set_order

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| view | object | はい |  |
| orderedFilterIds | int[] | はい |  |
| preserveUnlisted | bool | いいえ | true |
| detachViewTemplate | bool | いいえ | false |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "view_filter.set_order",
  "params": {
    "view": { "elementId": 12345 },
    "orderedFilterIds": [67890, 67891, 67892],
    "preserveUnlisted": true,
    "detachViewTemplate": false
  }
}
```

## 戻り値
- `finalOrder[]`: 反映後に検証した順序

## 関連コマンド
- view_filter.get_order
- view_filter.apply_to_view

