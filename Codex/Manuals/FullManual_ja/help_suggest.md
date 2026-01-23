# help_suggest

- カテゴリ: MetaOps
- 目的: 「何を実行すべきか」を決定論的に提案（レシピ + コマンド）。

## 概要
`help.suggest` は、日本語の問い合わせ文に対して、次の情報を使って候補を順位付きで返します。
- 現在の Revit コンテキスト（選択要素 / アクティブビュー）
- アドインのコマンド登録情報（`CommandMetadataRegistry`）
- 日本語グロッサリ（`glossary_ja.json`）

このコマンドは **モデル操作を実行しません**。提案を返すだけです。

## 使い方
- メソッド: `help.suggest`

### パラメータ
| 名前 | 型 | 必須 | 既定 |
|---|---|---:|---|
| queryJa | string | yes* |  |
| query | string | no |  |
| q | string | no |  |
| limit | integer | no | 5 |
| safeMode | boolean | no | true |
| includeContext | boolean | no | false |

補足:
- `queryJa` が推奨です。互換/利便性のため `query` / `q` も受け付けます。
- `safeMode=true` の場合、問い合わせが明確に「作成/変更/削除」意図を含まない限り、Write 系コマンドは順位が下がります。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "help.suggest",
  "params": {
    "queryJa": "部屋にW5を内張り。柱も拾って。既存はスキップ。",
    "limit": 5,
    "safeMode": true,
    "includeContext": true
  }
}
```

### 返却例（形）
```jsonc
{
  "ok": true,
  "code": "OK",
  "msg": "Suggestions",
  "data": {
    "normalized": { "actions": [], "entities": [], "concepts": [], "paramHints": {}, "unknownTerms": [] },
    "suggestions": [
      { "kind": "recipe", "id": "finish_wall_overlay_room_w5_v1", "method": "room.apply_finish_wall_type_on_room_boundary", "confidence": 0.86 }
    ],
    "didYouMean": [],
    "glossary": { "ok": true, "code": "OK", "path": "..." }
  }
}
```

## グロッサリの配置場所
アドインは `glossary_ja.json` を次の順で探索します（最初に見つかったものを使用）。
- `%LOCALAPPDATA%\\RevitMCP\\glossary_ja.json`
- `%USERPROFILE%\\Documents\\Codex\\Design\\glossary_ja.json`
- `<AddinFolder>\\Resources\\glossary_ja.json`
- `<AddinFolder>\\glossary_ja.json`
- もしくは環境変数 `REVITMCP_GLOSSARY_JA_PATH` を指定

`glossary_ja.json` が見つからない場合、同じ場所で `glossary_ja.seed.json` も探索します（ベストエフォート）。

## 関連
- search_commands（`help.search_commands`）
- describe_command（`help.describe_command`）
- get_context

