# set_material_asset / set_material_structural_asset / set_material_thermal_asset

- カテゴリ: ElementOps
- 目的: マテリアルに構造アセットまたは熱アセットを割り当てます。

## 概要
このコマンドは既存の PropertySetElement（構造／熱アセット）をマテリアルに関連付けます。個々の物性値を直接変更するのではなく、「どのアセットを使うか」を切り替える用途です。

## 使い方
- メソッド（エイリアス）:
  - set_material_asset
  - set_material_structural_asset
  - set_material_thermal_asset

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| materialId | int | 任意（uniqueId とどちらか必須） | 0 |
| uniqueId | string | 任意（materialId とどちらか必須） |  |
| assetKind | string | set_material_asset の場合は必須（structural / thermal） |  |
| assetId | int | 任意（assetName とどちらか必須） | 0 |
| assetName | string | 任意（assetId とどちらか必須） |  |

- set_material_structural_asset 使用時は `assetKind` が `"structural"` に固定されます。
- set_material_thermal_asset 使用時は `assetKind` が `"thermal"` に固定されます。

### リクエスト例（熱アセット）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_material_thermal_asset",
  "params": {
    "materialId": 99146,
    "assetId": 198872
  }
}
```

## 関連コマンド
- get_material_assets
- list_physical_assets
- list_thermal_assets

## 制限事項・推奨運用

- 一部の標準マテリアル／ライブラリアセットについては、MCP 経由でアセットを割り当てても、
  その後の物性値編集（熱伝導率など）が Revit 側の内部エラーで拒否される場合があります。
- とくに熱環境（Thermal）タブに関しては、
  - **どの Thermal アセットを使うか・どの値にするか** の決定は、基本的に **Revit の UI（マテリアルブラウザ）で行う** ことを前提にしてください。
  - `set_material_asset` は、ユーザーが UI で作成・複製したアセットを「紐づけ直す」「一括適用する」ための補助的なコマンドと考えてください。
  - コマンド実行後、期待どおり UI 上の挙動になっていない場合は、必ず UI でマテリアルのアセット状態を確認し、必要なら UI 側の操作を優先してください。
