# area_boundary_create_by_material_corecenter

- カテゴリ: Area
- 目的: 指定したマテリアル層の中心線（コア中心）に合わせて、壁からエリア境界線を作成します。

## 概要
**エリア平面（Area Plan）**で実行します。各壁について:
- 壁タイプの **CompoundStructure** を取得
- 指定 `materialId` に一致する層を抽出
- 対象層の決定ルール（既定 = Core 優先）:
  1) Core 範囲内に一致層があれば **Core 内だけ**で選定（Structure→最厚、無ければ最厚）
  2) それが無ければ `Function == Structure` の中で最厚
  3) それも無ければ一致層の中で最厚
- 壁の `LocationCurve` を対象層中心へオフセットし、**エリア境界線**を作成します。
- 反転壁（`Wall.Flipped == true`）にも対応しています（オフセット方向は `Wall.Orientation` を基準にし、可能なら外部側フェイス法線で整合チェックします）。

Core に一致がある場合でも仕上げ等の **非 Core 層**を候補に含めたい場合は、`options.include_noncore=true`（従来ルール）を指定してください。

指定マテリアルが壁タイプの層に存在しない壁は、既定ではスキップします。スキップせずに **コア中心線**（コアが無い場合は **壁芯**）へフォールバックしたい場合は `options.fallback_to_core_centerline=true` を指定してください。

カーテンウォール/積層壁、`LocationCurve` や `CompoundStructure` が無い壁は警告付きでスキップされます。

## 使い方
- メソッド: area_boundary_create_by_material_corecenter

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| materialId | int | はい* |  |
| materialName | string | はい* |  |
| material | object | はい* |  |
| wall_element_ids | int[] | いいえ/状況による | 選択 |
| viewId | int | いいえ | アクティブビュー |
| dryRun | bool | いいえ | false |
| refreshView | bool | いいえ | false |
| options.tolerance_mm | number | いいえ | 5.0 |
| options.skip_existing | bool | いいえ | true |
| options.include_debug | bool | いいえ | true |
| options.include_layer_details | bool | いいえ | false |
| options.include_noncore | bool | いいえ | false |
| options.fallback_to_core_centerline | bool | いいえ | false |

*`materialId` / `materialName` / `material:{id|name}` のいずれかを指定してください。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "area_boundary_create_by_material_corecenter",
  "params": {
    "material": { "name": "Concrete" },
    "wall_element_ids": [111, 222, 333],
    "viewId": 444,
    "dryRun": false,
    "options": { "tolerance_mm": 5.0, "skip_existing": true, "include_debug": true }
  }
}
```

## 結果（概要）
- `createdBoundaryLineIds`: 作成されたエリア境界線ID
- `results`: 壁ごとの結果（作成/スキップ + デバッグ）
- `warnings`: スキップ理由など
