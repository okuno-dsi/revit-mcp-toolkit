# set_material_thermal_conductivity

- カテゴリ: ElementOps
- 役割: マテリアルに紐づく Thermal アセットの熱伝導率（λ）だけを簡易に設定します。

## 概要
このコマンドは、マテリアルに割り当てられている ThermalAsset の `ThermalConductivity` プロパティを更新します。
λだけを手早く変えたいときのショートカットで、より本格的な物性編集には
`set_material_thermal_properties` を使うことを推奨します。

反映が不安定になりやすいケース（標準アセット／ライブラリアセット等）に備えて、内部では最大 3 段階で試行します。

1. 既存 ThermalAsset の直接編集（direct_edit）
2. Thermal PropertySetElement を複製して編集し、`Material.SetMaterialAspectByPropertySet(...)` で再バインド（duplicate_rebind）
3. 新規 ThermalAsset + 新規 PropertySetElement を作成して再バインド（create_new）

成功時は `strategy`（どの方式で成功したか）と `debug`（試行ログ）を返します。

## 呼び出し
- メソッド: set_material_thermal_conductivity

### パラメータ 
| 名前       | 型     | 必須 | 説明                             | 例           |
|------------|--------|------|----------------------------------|--------------|
| materialId | int    | ◯*  | 対象マテリアルの elementId      | `7255081`    |
| uniqueId   | string | ◯*  | 対象マテリアルの uniqueId       | `"..."`     |
| value      | double | ◯    | 熱伝導率の数値                  | `1.6`        |
| units      | string | 任意 | value の単位（省略時 W/(m·K)） | `"W/(m·K)"` |

`materialId` / `uniqueId` はどちらか一方があれば足ります。

- `value` / `units` で与えられた値は、内部的には **W/(m·K)** に変換されてから ThermalAsset に書き込まれます。
  - `"W/(m·K)"`, `"W/mK"` などはそのまま SI とみなされます。
  - `"BTU/(h·ft·°F)"` などを指定した場合は W/(m·K) に換算されます。

## Thermal アセットが未割り当て（thermal=null）の場合

`get_material_asset_properties` の結果が `"thermal": null` の場合、このコマンド単体では処理できないため、先に Thermal アセットを割り当てます。

実用上の手順（おすすめ）:
1. `list_thermal_assets` で「適当なテンプレート用 Thermal アセット」を選ぶ（どれでもOK。後段で置き換わる場合があります）
2. `set_material_thermal_asset` でその Thermal アセットをマテリアルに割り当てる
3. `set_material_thermal_conductivity` を実行する

標準アセット等で直接編集が内部エラーになる場合でも、このコマンドは最終的に **新規 Thermal アセットを作成して再バインド**（`strategy=create_new`）するため、λ を反映できるケースが増えます。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_material_thermal_conductivity",
  "params": {
    "materialId": 99146,
    "value": 1.6,
    "units": "W/(m·K)"
  }
}
```

## 返り値（形）

成功時（抜粋）:

```json
{
  "ok": true,
  "materialId": 99146,
  "uniqueId": "...",
  "strategy": "direct_edit",
  "normalized": { "W_per_mK": 1.6 },
  "stored": { "W_per_mK": 1.6, "internalValue": 0.48768 },
  "thermalAsset": { "assetId": 198872, "assetName": "..." },
  "debug": []
}
```

## 関連コマンド
- get_material_asset_properties
- set_material_asset
- set_material_thermal_properties

## 制限事項・UI での編集推奨について

- 一部の標準 Thermal アセットでは、このコマンドで `value` を設定しようとすると
  内部エラーになるケースがあります（direct_edit / duplicate_rebind が失敗）。
- このコマンドは 3 段階（direct_edit / duplicate_rebind / create_new）でできる限り反映を試みますが、
  それでも `ok:false` になる場合は `debug` の内容を確認し、**無理に繰り返さず Revit のマテリアルブラウザ上で熱伝導率を編集**してください。
- MCP 経由の λ 更新は、ユーザーが UI で複製・作成したアセットや、Revit が問題なく書き込みを許しているアセットに対する
  「補助的な自動化」として利用するのが安全です。最終的な基準値は UI 上の表示を優先してください。
