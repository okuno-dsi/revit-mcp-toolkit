# list_rebar_bar_types

- カテゴリ: Rebar
- 目的: プロジェクト内に存在する `RebarBarType`（鉄筋径/バータイプ）一覧を取得します。

## 使い方
- Method: `list_rebar_bar_types`

### パラメータ
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---:|---:|---|
| includeCountByDiameter | bool | no | true | `countByDiameterMm`（径ごとの件数）を付与します。 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "list_rebar_bar_types",
  "params": { "includeCountByDiameter": true }
}
```

## 注意
- 返り値の `count` が `0` の場合、当該プロジェクトに鉄筋タイプが存在しないため、配筋コマンドは作成できません。
- `import_rebar_types_from_document` で、別の `.rvt/.rte`（推奨: 構造テンプレート）から鉄筋タイプ（必要なら形状/フック）を取り込んでください。

