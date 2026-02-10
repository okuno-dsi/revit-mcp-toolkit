# element.cut_elements

- カテゴリ: Geometry / Cut
- 目的: 指定した切断要素で他の要素を切断（Cut Geometry）します。

## 概要
切断要素で指定要素を切断します。切断側には構造基礎も使用できます（Revit が許可する組み合わせに従います）。
`skipIfCannotCut=true` の場合、切断できない要素はスキップします。

## 使い方
- Method: element.cut_elements

### パラメータ
| 名前 | 型 | 必須 | デフォルト |
|---|---|---|---|
| cuttingElementId | int | はい |  |
| cuttingUniqueId | string | いいえ |  |
| cutElementIds | int[] | はい |  |
| cutElementUniqueIds | string[] | いいえ |  |
| cutElementId | int | いいえ |  |
| cutElementUniqueId | string | いいえ |  |
| skipIfAlreadyCut | bool | いいえ | true |
| skipIfCannotCut | bool | いいえ | true |

### 例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "element.cut_elements",
  "params": {
    "cuttingElementId": 12345,
    "cutElementIds": [23456, 34567],
    "skipIfAlreadyCut": true,
    "skipIfCannotCut": true
  }
}
```

## 結果
- `successIds`: 切断に成功した要素ID
- `skipped`: スキップ理由付きの要素
- `failed`: 失敗要素とエラー

## 関連
- element.uncut_elements
- element.join_elements
- element.unjoin_elements
