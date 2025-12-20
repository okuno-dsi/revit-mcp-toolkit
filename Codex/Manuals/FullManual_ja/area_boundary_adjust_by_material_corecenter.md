# area_boundary_adjust_by_material_corecenter

- カテゴリ: Area
- 目的: 選択した壁を基準に、既存のエリア境界線を指定マテリアル層の中心線（コア中心）へ移動します。

## 概要
**エリア平面（Area Plan）**で実行します。
- **Area + Wall** を指定（または `area_element_ids` / `wall_element_ids` を指定）
- 選択 Area の境界ループから境界線（Area Scheme Lines）を収集（安全に範囲を限定）
- 壁ごとにターゲット曲線（層中心）を計算し、近接・平行度でマッチした境界線を **削除+再作成** で置換（層選定ルールは `area_boundary_create_by_material_corecenter` と同じ）
- 境界線のマッチング許容（`options.tolerance_mm`）内で **近接 + 平行** により対応づけ、境界線を **削除+再作成** で置換
- 角の補正はベストエフォート（`trim_extend`、線分同士のみ）で、角補正用の許容（`options.corner_tolerance_mm`）を別途使用
- 反転壁（`Wall.Flipped == true`）にも対応しています（オフセット方向は `Wall.Orientation` を基準にし、可能なら外部側フェイス法線で整合チェックします）。
- 1本の壁が複数の部屋にまたがる等で「壁の基準線が境界線より長い」場合でも、マッチした境界線の範囲内に収まるよう **ターゲット曲線を境界線の端点に合わせてクリップ** してから置換します（暴走して閉じなくなるのを防止）。

Core に一致がある場合でも仕上げ等の **非 Core 層**を候補に含めたい場合は、`options.include_noncore=true`（従来ルール）を指定してください。

指定マテリアルが壁タイプの層に存在しない壁は、既定ではスキップします。スキップせずに **コア中心線**（コアが無い場合は **壁芯**）へフォールバックしたい場合は `options.fallback_to_core_centerline=true` を指定してください。

## 使い方
- メソッド: area_boundary_adjust_by_material_corecenter

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| materialId | int | はい* |  |
| materialName | string | はい* |  |
| material | object | はい* |  |
| area_element_ids | int[] | はい** |  |
| wall_element_ids | int[] | いいえ/状況による | 選択 |
| viewId | int | いいえ | アクティブビュー |
| dryRun | bool | いいえ | false |
| refreshView | bool | いいえ | false |
| options.tolerance_mm | number | いいえ | 5.0 |
| options.corner_tolerance_mm | number | いいえ | 自動（tolerance_mm&gt;10 のとき 5.0、そうでなければ tolerance_mm） |
| options.match_strategy | string | いいえ | nearest_parallel |
| options.corner_resolution | string | いいえ | trim_extend |
| options.parallel_threshold | number | いいえ | 0.98 |
| options.allow_view_wide | bool | いいえ | false |
| options.include_debug | bool | いいえ | true |
| options.include_layer_details | bool | いいえ | false |
| options.include_noncore | bool | いいえ | false |
| options.fallback_to_core_centerline | bool | いいえ | false |

*`materialId` / `materialName` / `material:{id|name}` のいずれかを指定してください。  
**安全のため、通常は `area_element_ids` が必要です（全ビュー対象にする場合のみ `options.allow_view_wide=true`）。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "area_boundary_adjust_by_material_corecenter",
  "params": {
    "material": { "name": "Concrete" },
    "area_element_ids": [777, 778],
    "wall_element_ids": [111, 222],
    "viewId": 444,
    "options": {
      "tolerance_mm": 5.0,
      "match_strategy": "nearest_parallel",
      "corner_resolution": "trim_extend",
      "parallel_threshold": 0.98
    }
  }
}
```

## 結果（概要）
- `adjustedBoundaryLineIdMap`: `{oldId,newId,wallId,...}` の配列
- `wallResults`: 壁ごとのマッチ結果
- `warnings`: スキップ理由など
- `toleranceMm`: マッチング許容（後方互換）  
- `matchToleranceMm` / `cornerToleranceMm`: マッチング / 角補正で実際に使用した許容値
