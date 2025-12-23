# set_material_thermal_properties

- カテゴリ: ElementOps
- 役割: マテリアルに紐づく Thermal アセットの **熱伝導率・密度・比熱** を一括で設定します。

## 概要

このコマンドは、マテリアルに割り当てられている Thermal PropertySetElement を取得し、その中の
`ThermalConductivity` / `Density` / `SpecificHeat` をまとめて更新します。

Thermal アセットがライブラリ由来などで「読み取り専用」の場合でも、内部でいったん

1. Thermal PropertySetElement を複製  
2. マテリアルにその複製を再バインド  
3. 複製側に対して物性値を書き込み

という処理を行うため、**Revit の UI で編集できない標準アセット**にも値を適用しやすくなっています。

## 呼び出し

- メソッド: `set_material_thermal_properties`

### パラメータ

| 名前                | 型      | 必須 | 説明                                        | 例                      |
|---------------------|---------|------|---------------------------------------------|-------------------------|
| `materialId`        | int     | ◯\* | 対象マテリアルの elementId（`uniqueId` とどちらか一方） | `7255081`               |
| `uniqueId`          | string  | ◯\* | 対象マテリアルの uniqueId                  | `"dc8a1c6b-..."`       |
| `properties`        | object  | ◯    | 設定したい物性値をまとめたオブジェクト      | 下記参照                |
| `conductivityUnits` | string  | 任意 | 熱伝導率の単位。省略時は W/(m·K)           | `"W/(m·K)"`, `"BTU/(h·ft·°F)"` |
| `densityUnits`      | string  | 任意 | 密度の単位（現状は `"kg/m3"` / `"kg/m^3"` のみ） | `"kg/m3"`               |
| `specificHeatUnits` | string  | 任意 | 比熱の単位（現状は `"J/(kg·K)"` 系のみ）     | `"J/(kg·K)"`            |

`materialId` / `uniqueId` はどちらか一方があれば足ります。

### `properties` オブジェクト

`properties` には、更新したい項目だけを含めます。プロパティ名は大文字・小文字を区別せず、いくつかの別名に対応します。

- 熱伝導率（λ）
  - キー候補: `"ThermalConductivity"`, `"thermalConductivity"`, `"lambda"`
  - 値: 数値（double）
  - 単位: `conductivityUnits` で指定（省略時 W/(m·K)）
- 密度
  - キー候補: `"Density"`, `"density"`
  - 値: 数値（double）
  - 単位: `densityUnits`（現状は SI 入力 `"kg/m3"` 前提で、内部単位へ変換して反映）
- 比熱
  - キー候補: `"SpecificHeat"`, `"specificHeat"`, `"Cp"`
  - 値: 数値（double）
  - 単位: `specificHeatUnits`（現状は SI 入力 `"J/(kg·K)"` 前提で、内部単位へ変換して反映）

## 例: 熱伝導率だけを 1.6 W/(m·K) に設定

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_material_thermal_properties",
  "params": {
    "materialId": 7255081,
    "properties": {
      "ThermalConductivity": 1.6
    },
    "conductivityUnits": "W/(m·K)"
  }
}
```

この場合、ThermalAsset の `ThermalConductivity` のみが 1.6 W/(m·K) に更新されます。

## 例: λ・密度・比熱をまとめて設定

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_material_thermal_properties",
  "params": {
    "materialId": 7255081,
    "properties": {
      "lambda": 1.6,
      "Density": 2400,
      "Cp": 840
    },
    "conductivityUnits": "W/(m·K)",
    "densityUnits": "kg/m3",
    "specificHeatUnits": "J/(kg·K)"
  }
}
```

## 返り値

成功時の例（抜粋）:

```json
{
  "ok": true,
  "materialId": 7255081,
  "uniqueId": "dc8a1c6b-7eff-48ec-92d3-cd60e2dca22b-006eb429",
  "updated": {
    "ThermalConductivity_W_per_mK": 1.6,
    "Density_kg_per_m3": 2400.0,
    "SpecificHeat_J_per_kgK": 840.0
  },
  "duplicatedAsset": false
}
```

- `duplicatedAsset` が `true` の場合は、元の Thermal PropertySetElement が読み取り専用だったため、
  MCP 側で **複製＆再バインド** を行ったことを意味します。

## 注意点

- Thermal アセットがまだ割り当てられていないマテリアルに対しては、先に `set_material_asset` で
  `assetKind = "thermal"` のアセットを割り当てておいてください。
- 熱伝導率・密度・比熱は入力単位（現状は SI 想定）から Revit 内部単位へ変換して書き込みます。
  - `densityUnits` は現状 `"kg/m3"` / `"kg/m^3"` のみ対応です。
  - `specificHeatUnits` は現状 `"J/(kg·K)"`（および表記揺れ）を想定しています。
- `get_material_asset_properties` と組み合わせると、設定した値が Revit API 上どう見えているか確認できます。

## 関連コマンド

- `set_material_asset` / `set_material_thermal_asset`  
  Thermal アセットの割り当て
- `set_material_thermal_conductivity`  
  熱伝導率だけを簡易に設定したい場合のショートカット（内部で ThermalAsset を直接更新）
- `get_material_asset_properties`  
  Thermal/Structural アセットの物性値を一覧で取得

## 制限事項・UI での編集推奨について

- Revit の標準マテリアル／ライブラリアセットの中には、API から ThermalAsset を更新しようとすると
  「内部エラー」が返されたり、値が書き換わらないものがあります。
- そのような場合、このコマンドは `ok:false` や `An internal error has occurred.` を返します。
  **その際は無理に繰り返さず、Revit の UI（マテリアルブラウザ）上で値を編集する運用を推奨** します。
- MCP からの Thermal 編集は、主に
  - ユーザーが UI で複製・作成した独自アセット
  - Revit が問題なく書き込みを許しているアセット

  を対象とした「補助的な自動化」と考えてください。基準値の設定や確認は、最終的に UI での見え方を優先してください。
