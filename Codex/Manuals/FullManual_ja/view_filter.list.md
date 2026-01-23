# view_filter.list

- カテゴリ: ViewFilterOps
- 目的: プロジェクト内のビュー用フィルタ（パラメータ/選択）一覧を取得します。

## 概要
`ParameterFilterElement` と `SelectionFilterElement` を列挙し、ID・名前・カテゴリ等の概要を返します。

## 使い方
- メソッド: view_filter.list

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| kinds | string[] | いいえ | ["parameter","selection"] |
| namePrefix | string | いいえ |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "view_filter.list",
  "params": {
    "kinds": ["parameter"],
    "namePrefix": "A-"
  }
}
```

## 戻り値
- `filters[]`: `{ elementId, uniqueId, kind, name, categories[], ruleCount, elementIdCount }`

## 関連コマンド
- view_filter.upsert
- view_filter.delete
- view_filter.apply_to_view
- view_filter.remove_from_view
- view_filter.get_order
- view_filter.set_order

