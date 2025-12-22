# search_commands

- カテゴリ: MetaOps
- 目的: 利用可能なコマンドを検索（順位付け）する

## 概要
Add-in のランタイムコマンドメタ情報レジストリを使って、コマンド名を検索します。

- エイリアス: `help.search_commands`
- Step 4: 検索結果の `name` は **ドメイン先頭の正規名**（例: `doc.get_project_info`）になり、従来名は `aliases` として残り、引き続き呼び出せます。

## 使い方
- メソッド: `search_commands`

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| query | string | *いいえ |  |
| tags | string[] | *いいえ |  |
| riskMax | string | いいえ |  |
| limit | integer | いいえ | 10 |
| category | string | いいえ |  |
| kind | string | いいえ |  |
| importance | string | いいえ |  |
| prefixOnly | boolean | いいえ | false |
| q | string | いいえ（互換） |  |
| top | integer | いいえ（互換） |  |

補足:
- Step 3 仕様の入力は `query` / `tags` です。どちらか一方以上が必須です。
- `q`/`top` は後方互換（`query`/`limit` の別名）です。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "help.search_commands",
  "params": {
    "query": "place view on sheet",
    "tags": ["sheet", "place"],
    "limit": 10,
    "riskMax": "medium"
  }
}
```

### 戻り値（例・形）
```jsonc
{
  "ok": true,
  "code": "OK",
  "msg": "Top matches",
  "data": {
    "items": [
      { "name": "sheet.place_view_auto", "score": 0.93, "summary": "Place a view; auto-duplicate if needed", "risk": "medium", "tags": ["sheet","place","auto"] }
    ]
  }
}
```
