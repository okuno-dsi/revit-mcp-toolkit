# describe_command

- カテゴリ: MetaOps
- 目的: コマンドの説明（メタ情報の取得）

## 概要
Add-in が保持するランタイムのコマンドメタ情報レジストリから、コマンドの分類や危険度などの情報を返します。
あわせて、エージェントが扱いやすいヒントを返します。
- `paramsSchema` / `resultSchema`（JSON Schema。現状は緩いフォールバック）
- `exampleJsonRpc`
- `commonErrorCodes`
- （任意）`term_map_ja.json` が利用できる場合は `terminology`（同義語 / 除外語 / 出典）

- エイリアス: `help.describe_command`
- Step 4: `data.name` は **ドメイン先頭の正規名**です。従来名（例: `get_project_info`）で指定しても同じ結果に解決され、`data.aliases` に残ります。

## 使い方
- メソッド: `describe_command`

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| method | string | はい |  |

補足:
- `name` または `command` も `method` の代わりに指定できます（互換用）。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "help.describe_command",
  "params": { "method": "element.get_walls" }
}
```

### 戻り値（例・形）
```jsonc
{
  "ok": true,
  "code": "OK",
  "msg": "Command description",
  "data": {
    "name": "element.get_walls",
    "category": "ElementOps/Wall",
    "kind": "read",
    "importance": "normal",
    "risk": "low",
    "tags": ["ElementOps", "Wall"],
    "aliases": ["get_walls"],
    "paramsSchema": { "type": "object", "additionalProperties": true },
    "resultSchema": { "type": "object", "additionalProperties": true },
    "exampleJsonRpc": "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"element.get_walls\", \"params\":{} }",
    "commonErrorCodes": [
      { "code": "INVALID_PARAMS", "msg": "Missing/invalid parameters" },
      { "code": "UNKNOWN_COMMAND", "msg": "No such command" }
    ],
    "terminology": {
      "term_map_version": "xxxxxxxx",
      "synonyms": ["断面", "セクション"],
      "negative_terms": ["平断面"],
      "sources": ["view:SECTION_VERTICAL"]
    }
  }
}
```
