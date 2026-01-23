# get_project_units

- カテゴリ: DocumentOps
- 種別: read
- 目的: プロジェクトの単位設定（Project Units / FormatOptions）を取得します。

Revit の UI での **表示単位（Length/Area/Volume/Angle など）** を確認したいときに使います。

## コマンド
- Canonical: `doc.get_project_units`
- 別名: `get_project_units`

## パラメータ
| 名前 | 型 | 必須 | 既定値 | メモ |
|---|---|---:|---:|---|
| mode | string | no | `common` | `common` または `all`。 |
| specNames | string[] | no | `[]` | `SpecTypeId` のプロパティ名（例: `["Length","Area","Volume","Angle","Slope"]`）。指定した場合は `mode` より優先。 |
| includeLabels | bool | no | `true` | `LabelUtils` によるラベル取得（best-effort / 空になる場合あり）。 |
| includeExamples | bool | no | `true` | `exampleFromInternal_1` を追加（簡易サニティチェック）。 |

## 返す内容
- `displayUnitSystem`（例: Metric / Imperial）
- `items[]`: spec ごとの `specTypeId` / `unitTypeId` と、補助情報（丸め/精度など）

## リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "doc.get_project_units",
  "params": {
    "mode": "common"
  }
}
```

## 注意
- Revit の内部単位は spec ごとに固定です（例: Length=ft, Area=ft², Volume=ft³）。
- `exampleFromInternal_1` は `UnitUtils.ConvertFromInternalUnits(1.0, unitTypeId)` の結果です（spec によっては線形換算ではありません）。

