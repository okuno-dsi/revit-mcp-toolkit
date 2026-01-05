# rebar_mapping_resolve

- カテゴリ: Rebar
- 目的: `RebarMapping.json` の論理キーを、選択したホスト要素に対して解決し、結果を返します（デバッグ/検証用）。

## 概要
自動配筋コマンドを作る前に、マッピング設定が正しく値を拾えているか確認する用途です。

読み込みはベストエフォートで、次の順で探索します:
- `REVITMCP_REBAR_MAPPING_PATH`（明示指定）
- `%LOCALAPPDATA%\\RevitMCP\\RebarMapping.json`
- `%USERPROFILE%\\Documents\\Codex\\Design\\RebarMapping.json`
- アドインDLLと同じフォルダ（既定の同梱場所）

### プロファイル自動選択（`profile` 未指定時）
ベストエフォートで次の情報を見てプロファイルを選びます:
- `appliesTo.categories`
- 任意: `appliesTo.familyNameContains` / `appliesTo.typeNameContains`
- 任意: `appliesTo.requiresTypeParamsAny` / `appliesTo.requiresInstanceParamsAny`
- その後 `priority`（大きいほど優先）→ より具体的なプロファイルが優先

### `sources[].kind` の種類
- `constant`, `derived`, `instanceParam`, `typeParam`, `builtInParam`
- `instanceParamGuid`, `typeParamGuid`（共有パラメータGUID指定で言語差を吸収）

### 数値（mm）変換の注意
`double` / `int` で `unit:\"mm\"` を扱う場合:
- パラメータの Spec が `Length` のときは内部単位（ft → mm）に変換
- `Length` 以外は「格納値をそのまま mm」とみなす（RC系ファミリで mm を数値として持つケースを想定）

## 使い方
- Method: `rebar_mapping_resolve`

### パラメータ
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---:|---:|---|
| hostElementIds | int[] | no |  | ホスト要素ID（柱/梁など）。 |
| useSelectionIfEmpty | bool | no | true | `hostElementIds` が空の場合、選択要素から取得します。 |
| profile | string | no |  | プロファイル名（未指定ならカテゴリから自動選択し `default` にフォールバック）。 |
| keys | string[] | no |  | 未指定なら、プロファイルに定義された全キーを解決します。 |
| includeDebug | bool | no | false | true の場合、各キーがどのソースで解決されたかを返します。 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "rebar_mapping_resolve",
  "params": {
    "useSelectionIfEmpty": true,
    "profile": "default",
    "keys": ["Host.Section.Width", "Host.Cover.Other"],
    "includeDebug": true
  }
}
```

## 関連
- `rebar_layout_inspect`
