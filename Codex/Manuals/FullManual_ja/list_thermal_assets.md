# list_thermal_assets

- カテゴリ: ElementOps
- 目的: プロジェクト内で利用可能な熱アセット一覧を取得します。

## 概要
このコマンドは ThermalAsset を内包する PropertySetElement を列挙し、マテリアルに割り当て可能な熱アセットの候補を確認するために使用します。

## 使い方
- メソッド: list_thermal_assets

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| nameContains | string | 任意 |  |
| skip | int | 任意 | 0 |
| count | int | 任意 | 2147483647 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "list_thermal_assets",
  "params": {
    "nameContains": "コンクリート",
    "skip": 0,
    "count": 50
  }
}
```

## 関連コマンド
- list_physical_assets
- get_material_assets
- set_material_asset

