# set_view_template

- カテゴリ: ViewOps
- 目的: このコマンドは『set_view_template』を設定します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: set_view_template

### パラメータ
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---|---|---|
| viewId | int | （view 指定のいずれか必須） | なし | 対象ビューの ElementId。 |
| uniqueId | string | 同上 | なし | 対象ビューの UniqueId。（`viewId` の代わりに使用可） |
| templateViewId | int | （テンプレート指定のいずれか必須※） | なし | 適用するビューテンプレートの ElementId。テンプレートビューである必要があります。 |
| templateName | string | 同上 | なし | 適用するビューテンプレート名（大文字小文字は無視）。`templateViewId` の代わりに使用可。 |
| clear | bool | いいえ/状況による | false | `true` の場合はビューテンプレートを解除し、`templateViewId` / `templateName` は無視されます。 |

※ `clear:false` でテンプレートを適用する場合は、`templateViewId` か `templateName` のどちらかが必要です。

### リクエスト例（テンプレートを適用）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_view_template",
  "params": {
    "viewId": 123456,
    "templateViewId": 234567
  }
}
```

### リクエスト例（テンプレートを名前で適用）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_view_template",
  "params": {
    "viewId": 123456,
    "templateName": "意匠_平面_標準"
  }
}
```

### リクエスト例（テンプレートを解除）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_view_template",
  "params": {
    "viewId": 123456,
    "clear": true
  }
}
```

## 関連コマンド
## 関連コマンド
- get_view_info
- save_view_state
- restore_view_state
- create_view_plan
- create_section
- create_elevation_view
- compare_view_states
- 
