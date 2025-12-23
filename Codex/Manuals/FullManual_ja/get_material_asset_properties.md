# get_material_asset_properties

- カテゴリ: ElementOps
- 目的: マテリアルに紐づく構造／熱アセットの物性値（パラメータ名・単位・値・ID）を取得します。

## 概要
このコマンドは、Material が参照している StructuralAsset と ThermalAsset を調べ、それぞれの公開プロパティを汎用的な形で返します。解析・ドキュメント用を想定しており、結果はそのまま JSON として扱えます。

## 使い方
- メソッド: get_material_asset_properties

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| materialId | int | 任意（uniqueId とどちらか必須） | 0 |
| uniqueId | string | 任意（materialId とどちらか必須） |  |
| assetKind | string | 任意（\"structural\" / \"thermal\"。省略時は両方） |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_material_asset_properties",
  "params": {
    "materialId": 99146,
    "assetKind": "thermal"
  }
}
```

## 関連コマンド
- get_material_assets
- list_physical_assets
- list_thermal_assets
- set_material_thermal_conductivity
