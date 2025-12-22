# revit.batch

- カテゴリ: Meta
- 目的: 複数コマンドを 1 回の Revit ExternalEvent でまとめて実行し、RPC 往復回数とオーバーヘッドを削減します。

## 概要
- Canonical: `revit.batch`
- 旧エイリアス: `revit_batch`

`ops[]` を上から順に実行し、各 op の結果を 1 回のレスポンスで返します。

## 使い方
- Method: `revit.batch`

### パラメータ
| 名前 | 型 | 必須 | 既定 |
|---|---|---|---|
| ops | array | yes |  |
| transaction | string | no | `single` |
| dryRun | boolean | no | false |
| stopOnError | boolean | no | true |

`transaction`:
- `single`: 全 op を 1 つの `TransactionGroup` にまとめます。op が失敗した場合（または `dryRun=true`）はロールバックします。
- `perOp`: op ごとに `TransactionGroup` を作成します。失敗した op はロールバックし、成功した op はコミットします（ただし `dryRun=true` の場合はロールバック）。
- `none`: トランザクションのグルーピングをしません（`dryRun=true` はエラーにします）。

注意点 / 制約:
- `dryRun=true` は `transaction=single` または `perOp` が必要です。
- `ops[]` 内に `revit.batch` を入れる（ネスト）は不可です。
- `transaction != "none"` のとき、op 内でのドキュメント切替（`params.documentPath`）は不可です。
- MCP Ledger 上は「バッチ全体で 1 コマンド」として扱われ、op ごとの ledger 更新は行いません。
- ビュー切替など、一部の操作はトランザクション外の副作用があり、ロールバックできません。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "revit.batch",
  "params": {
    "transaction": "single",
    "dryRun": true,
    "ops": [
      { "method": "doc.get_project_info", "params": {} },
      { "method": "view.get_current_view", "params": {} }
    ]
  }
}
```

### 戻り値例（形）
```jsonc
{
  "ok": true,
  "code": "OK",
  "data": {
    "transaction": "single",
    "dryRun": true,
    "rolledBack": true,
    "opCount": 2,
    "okCount": 2,
    "failCount": 0,
    "results": [
      { "index": 0, "method": "doc.get_project_info", "result": { "ok": true, "code": "OK", "data": {} } },
      { "index": 1, "method": "view.get_current_view", "result": { "ok": true, "code": "OK", "data": {} } }
    ]
  }
}
```

## 関連
- list_commands
- help.search_commands
- help.describe_command

