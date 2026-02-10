# search_commands

- カテゴリ: MetaOps
- 目的: 利用可能なコマンドを検索（順位付け）する

## 概要
Add-in のランタイムコマンドメタ情報レジストリを使って、コマンド名を検索します。

- エイリアス: `help.search_commands`
- Step 4: 検索結果の `name` は **ドメイン先頭の正規名**（例: `doc.get_project_info`）になり、従来名は `aliases` として残り、引き続き呼び出せます。
- デフォルトは正規名のみを返します。`includeDeprecated=true` を指定すると、従来名（エイリアス）も **deprecated** として個別に返します（各 `items[]` に `deprecated=true` が付きます）。

## 用語・同義語に強い検索（term_map_ja.json）
`term_map_ja.json` が利用できる場合、`search_commands` は日本語の同義語と曖昧さ解消ルールで順位付けを補強します。

代表例:
- `断面` / `セクション` ⇒ `create_section`（立断面）
- `平断面` / `平面図` / `伏図` ⇒ `create_view_plan`（平面）
- `立面` ⇒ `create_elevation_view`
- `RCP` / `天井伏図` ⇒ `create_view_plan`（必要なら `suggestedParams` に `view_family=CeilingPlan` 等のヒントが入ります）

### term_map_ja.json の配置場所
Add-in は次の順に `term_map_ja.json` を探します（見つかったものを使用）:
- `%LOCALAPPDATA%\RevitMCP\term_map_ja.json`
- `%USERPROFILE%\Documents\Codex\Design\term_map_ja.json`
- `<AddinFolder>\Resources\term_map_ja.json`
- `<AddinFolder>\term_map_ja.json`
- もしくは環境変数 `REVITMCP_TERM_MAP_JA_PATH`

### 戻り値の追加フィールド
用語マップがヒットした場合、`data.items[]` に次のフィールドが追加されることがあります:
- `termScore` / `matched` / `hint` / `suggestedParams`

また、`data.termMap` に `term_map_version` と、簡潔な既定/曖昧さ解消サマリが入ります。

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
| includeDeprecated | boolean | いいえ | false |
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
      { "name": "sheet.place_view_auto", "score": 0.93, "summary": "Place a view; auto-duplicate if needed", "risk": "medium", "tags": ["sheet","place","auto"], "deprecated": false }
    ]
  }
}
```

### 簡易テスト用スクリプト
- `Scripts/Reference/test_terminology_routing.ps1 -Port 5210`



