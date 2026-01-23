# view_filter.delete

- カテゴリ: ViewFilterOps
- 目的: フィルタ定義（プロジェクト全体）を削除します。

## 概要
`FilterElement`（パラメータフィルタ/選択フィルタ）をドキュメントから削除します。

## 使い方
- メソッド: view_filter.delete

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| filter | object | はい |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "view_filter.delete",
  "params": {
    "filter": { "name": "A-WALLS-RC" }
  }
}
```

## 注意
- 削除対象が多くのビュー/テンプレートに適用されている場合、影響が大きくなります。

## 関連コマンド
- view_filter.list
- view_filter.upsert

