# get_material_assets

- カテゴリ: ElementOps
- 目的: 指定したマテリアルに紐づいている構造／熱アセット情報を取得します。

## 概要
このコマンドは JSON-RPC を介して実行され、マテリアルに現在割り当てられている PropertySetElement（構造・熱）の ID と名前を返します。

## 使い方
- メソッド: get_material_assets

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| materialId | int | 任意（uniqueId とどちらか必須） | 0 |
| uniqueId | string | 任意（materialId とどちらか必須） |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_material_assets",
  "params": {
    "materialId": 99146
  }
}
```

## 関連コマンド
- list_physical_assets
- list_thermal_assets
- set_material_asset
- get_material_asset_properties

