# get_dwg_export_setup

- カテゴリ: Export
- 目的: 指定した DWG 出力設定の内容を取得します。

## 概要
- DWG 出力設定のトップレベル設定と、カテゴリ別レイヤ表を取得します。
- 必要に応じて、線種表、フォント表、ハッチパターン表も取得できます。
- 主な用途は「どのカテゴリがどのレイヤ名・色番号・cutレイヤ名で出力されるか」の確認です。

## メソッド
- `get_dwg_export_setup`

## パラメータ
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---|---|---|
| name | string | いいえ | アクティブ設定 | 取得対象の設定名。省略時はアクティブ設定を取得します。 |
| includeLayers | bool | いいえ | true | カテゴリ別レイヤ表を返します。 |
| includeLineTypes | bool | いいえ | true | Revit 線種名 → 出力線種名の対応表を返します。 |
| includeFonts | bool | いいえ | false | Revit フォント名 → 出力フォント名の対応表を返します。 |
| includePatterns | bool | いいえ | false | Revit パターン名 → 出力パターン名の対応表を返します。 |
| categoryContains | string | いいえ |  | レイヤ表のカテゴリ名フィルタです。部分一致です。 |
| subCategoryContains | string | いいえ |  | レイヤ表のサブカテゴリ名フィルタです。部分一致です。 |
| limit | int | いいえ | 0 | レイヤ表の返却件数上限です。`0` は無制限です。 |

## 主なレスポンス項目
- `options`
  - `fileVersion`
  - `colors`
  - `layerMapping`
  - `propOverrides`
  - `lineScaling`
  - `linetypesFileName`
  - `hatchPatternsFileName`
  - `targetUnit`
  - `sharedCoords`
  - `mergeViews`
- `tables.layers.items[]`
  - `categoryName`
  - `subCategoryName`
  - `layerName`
  - `cutLayerName`
  - `colorNumber`
  - `cutColorNumber`
  - `colorName`
  - `layerModifiers[]`
  - `cutLayerModifiers[]`
- `tables.lineTypes.items[]`
  - `originalLinetypeName`
  - `destinationLinetypeName`

## 例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_dwg_export_setup",
  "params": {
    "name": "社内標準_DWG",
    "includeLayers": true,
    "includeLineTypes": true,
    "categoryContains": "構造",
    "limit": 100
  }
}
```

## 注意
- 現時点では読取中心です。
- 設定の複製、名称変更、削除、更新は別コマンドとして追加する想定です。
