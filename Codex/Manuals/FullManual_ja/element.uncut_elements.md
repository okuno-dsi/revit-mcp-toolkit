# element.uncut_elements

- カテゴリ: Geometry / Cut
- 目的: Cut Geometry の解除を行います。

## 概要
切断要素と切断された要素の関係を解除します。

## 使い方
- Method: element.uncut_elements

### パラメータ
| 名前 | 型 | 必須 | デフォルト |
|---|---|---|---|
| cuttingElementId | int | はい |  |
| cuttingUniqueId | string | いいえ |  |
| cutElementIds | int[] | はい |  |
| cutElementUniqueIds | string[] | いいえ |  |
| cutElementId | int | いいえ |  |
| cutElementUniqueId | string | いいえ |  |

### 例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "element.uncut_elements",
  "params": {
    "cuttingElementId": 12345,
    "cutElementIds": [23456, 34567]
  }
}
```

## 結果
- `successIds`: 解除に成功した要素ID
- `failed`: 失敗要素とエラー

## 関連
- element.cut_elements
- element.join_elements
- element.unjoin_elements
