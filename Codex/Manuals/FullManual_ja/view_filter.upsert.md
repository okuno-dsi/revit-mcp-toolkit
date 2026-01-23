# view_filter.upsert

- カテゴリ: ViewFilterOps
- 目的: ビュー用フィルタ定義（プロジェクト全体）を作成/更新します。

## 概要
フィルタ定義を作成または更新します。
- `kind="parameter"` → `ParameterFilterElement`（標準化用途に推奨）
- `kind="selection"` → `SelectionFilterElement`（要素集合を手選別する用途）

例外が出やすいポイント（カテゴリやパラメータの不一致）を事前に検証し、失敗時は理由を返します。
`parameterName` は曖昧になりやすいので、可能なら `builtInParameter` / `sharedParameterGuid` を使ってください。

## 使い方
- メソッド: view_filter.upsert

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| definition | object | はい |  |

### 例（パラメータフィルタ・文字列 contains）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "view_filter.upsert",
  "params": {
    "definition": {
      "kind": "parameter",
      "name": "A-WALLS-RC",
      "categories": ["OST_Walls"],
      "logic": "and",
      "rules": [
        {
          "parameter": {
            "type": "builtInParameter",
            "builtInParameter": "ALL_MODEL_INSTANCE_COMMENTS"
          },
          "operator": "contains",
          "value": { "kind": "string", "text": "RC", "caseSensitive": false }
        }
      ]
    }
  }
}
```

### 例（選択フィルタ）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "view_filter.upsert",
  "params": {
    "definition": {
      "kind": "selection",
      "name": "TEMP-HIGHLIGHT-001",
      "elementIds": [123, 456]
    }
  }
}
```

## 関連コマンド
- view_filter.list
- view_filter.delete
- view_filter.apply_to_view

