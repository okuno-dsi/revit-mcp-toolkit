# meta.resolve_category

- カテゴリ: MetaOps
- 目的: 曖昧なカテゴリ名を BuiltInCategory（OST_*）に解決します。

## 概要
「通り心」「柱」などの曖昧な入力を、安定したカテゴリID（例: `OST_Grids`）へ解決するヘルパーです。  
文字化け（mojibake）にもベストエフォートで対応し、曖昧な場合は候補を返します。

- 辞書ファイル: `category_alias_ja.json`（アドイン直下、UTF-8/BOMなし）
- 曖昧な場合は常に `ok=false` で候補を返します。

## 使い方
- メソッド: meta.resolve_category

### パラメータ
| 名前 | 型 | 必須 | 説明 |
|---|---|---|---|
| text | string | はい | 解決したい入力テキスト |
| maxCandidates | int | いいえ | 最大候補数（既定: 5） |
| context | object | いいえ | スコア補正用の文脈情報 |

`context` フィールド:
- `selectedCategoryIds`: string[] / int[]  
  - 文字列は `OST_*` を想定  
  - 数値は可能であれば BuiltInCategory に変換
- `disciplineHint`: string（例: `Structure`）
- `activeViewType`: string（任意）

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": "1",
  "method": "meta.resolve_category",
  "params": {
    "text": "通り心",
    "context": {
      "selectedCategoryIds": ["OST_Grids"],
      "disciplineHint": "Structure",
      "activeViewType": "FloorPlan"
    },
    "maxCandidates": 5
  }
}
```

## レスポンス
```jsonc
{
  "ok": true,
  "msg": null,
  "normalizedText": "通り心",
  "recoveredFromMojibake": false,
  "resolved": {
    "id": "OST_Grids",
    "builtInId": -2000220,
    "labelJa": "通り芯（グリッド）",
    "score": 0.93,
    "reason": "alias exact match; context selectedCategoryIds"
  },
  "candidates": [
    { "id": "OST_Grids", "builtInId": -2000220, "labelJa": "通り芯（グリッド）", "score": 0.93 }
  ],
  "dictionary": {
    "ok": true,
    "path": "...\\RevitMCPAddin\\category_alias_ja.json",
    "version": "2026-01-20",
    "lang": "ja-JP",
    "schemaVersion": 1,
    "sha8": "8c2f5a1b"
  }
}
```

## 注意
- 解決後は、`id`（例: `OST_Grids`）をコマンドのカテゴリ指定に使ってください。
- `ok=false` の場合は、`candidates` からユーザーに確認してください。
