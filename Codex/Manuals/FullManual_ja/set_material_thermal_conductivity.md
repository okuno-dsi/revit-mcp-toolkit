# set_material_thermal_conductivity

- カテゴリ: ElementOps
- 役割: マテリアルに紐づく Thermal アセットの熱伝導率（λ）だけを簡易に設定します。

## 概要
このコマンドは、マテリアルに割り当てられている ThermalAsset の `ThermalConductivity` プロパティを更新します。
λだけを手早く変えたいときのショートカットで、より本格的な物性編集には
`set_material_thermal_properties` を使うことを推奨します。

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

## 関連コマンド
- get_material_asset_properties
- set_material_asset
- set_material_thermal_properties

## 制限事項・UI での編集推奨について

- 一部の標準 Thermal アセットでは、このコマンドで `value` を設定しようとすると
  内部エラーになったり、値が反映されないケースがあります。
- そのような場合は、**無理に繰り返さず Revit のマテリアルブラウザ上で熱伝導率を編集**してください。
- MCP 経由の λ 更新は、ユーザーが UI で複製・作成したアセットや、Revit が問題なく書き込みを許しているアセットに対する
  「補助的な自動化」として利用するのが安全です。最終的な基準値は UI 上の表示を優先してください。
