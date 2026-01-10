# list_rebar_hook_types

- カテゴリ: Rebar
- 目的: プロジェクト内の `RebarHookType`（フックタイプ）を一覧表示します。

## 使い方
- メソッド: `list_rebar_hook_types`

### パラメータ
| 名前 | 型 | 必須 | 既定 | 備考 |
|---|---|---:|---:|---|
| includeCountByAngle | bool | no | true | `countByAngleDeg`（度を整数丸めした件数サマリ）を付与します。 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "list_rebar_hook_types",
  "params": { "includeCountByAngle": true }
}
```

## 注意点
- フック名称・種類は **プロジェクト/テンプレート依存**です。
- `RebarStyle`（標準/帯筋・スターラップ）によって、使用できるフックが制限される場合があります。帯筋/スターラップ（`RebarStyle.StirrupTie`）を作成する場合は、`StirrupTie` に適合するフックを選んでください。

