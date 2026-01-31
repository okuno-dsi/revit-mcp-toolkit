# param.transfer_values

- カテゴリ: ParamOps
- 目的: 複数要素のパラメータ値を、別のパラメータへ転記する（文字列操作対応）。

## 概要
選択・ID・カテゴリ・全要素に対して、**source** から **target** へ値を転記します。文字列の上書き/追記/置換/検索にも対応します。インスタンス/タイプの自動解決を行います。

## 使い方
- Method: param.transfer_values
- エイリアス: param_transfer_values, transfer_parameter_values

### パラメータ（例）
```jsonc
{
  "elementIds": [1001, 1002],
  "source": { "paramName": "ホスト カテゴリ" },
  "target": { "paramName": "コメント" },
  "stringOp": "overwrite",
  "dryRun": false
}
```

### 主要パラメータ
- `source` / `target` (object, required)
  - 例: `{ "paramName": "コメント" }` / `{ "builtInId": -1002001 }` / `{ "guid": "..." }`
- `stringOp` (string): overwrite / append / replace / search
- `sourceTarget` / `targetTarget` (string): auto / instance / type
- `preferSource` / `preferTarget` (string): instance / type
- `dryRun` (bool): true のとき書き込みを行わず結果のみ返す
- `nullAsError` (bool): 値なしをエラー扱いにする（既定 true）

### 出力（概要）
```jsonc
{
  "ok": true,
  "updatedCount": 25,
  "failedCount": 2,
  "skippedCount": 3,
  "scopeUsed": "selection",
  "items": [ { "ok": true, "elementId": 1001 } ]
}
```

## 注意
- `auto` の場合、同名パラメータがインスタンス/タイプ双方にあると **インスタンス優先** です。
- 文字列以外のターゲットでは `stringOp=overwrite` のみ対応します。
- 大量要素は `maxMillisPerTx` で分割してトランザクションを回します。

- 英語版: `../FullManual/param.transfer_values.md`
