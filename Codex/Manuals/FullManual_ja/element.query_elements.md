# element.query_elements

- カテゴリ: ElementOps/Query
- 目的: 構造化フィルタ（カテゴリ/クラス名/レベル/名前/BoundingBox/パラメータ条件）で要素を抽出します。

## 概要
キーワード検索よりも確実に対象を絞り込みたい場合に使用します。

典型手順:
1) `element.query_elements` で候補IDを抽出  
2) 必要なら read コマンドで確認  
3) write コマンド（更新/移動/着色等）へ渡す  

## 使い方
- メソッド: `element.query_elements`

### パラメータ
トップレベル:
```jsonc
{
  "scope": { "viewId": 0, "includeHiddenInView": false },
  "filters": {
    "categories": [],
    "classNames": [],
    "levelId": 0,
    "name": { "mode": "contains", "value": "", "caseSensitive": false },
    "bbox": {
      "min": { "x": 0, "y": 0, "z": 0, "unit": "mm" },
      "max": { "x": 0, "y": 0, "z": 0, "unit": "mm" },
      "mode": "intersects"
    },
    "parameters": []
  },
  "options": {
    "includeElementType": true,
    "includeBoundingBox": false,
    "includeParameters": [],
    "maxResults": 200,
    "orderBy": "id"
  }
}
```

#### `scope`
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| viewId | int | いいえ | null |
| includeHiddenInView | bool | いいえ | false |

#### `filters`
| 名前 | 型 | 必須 | 補足 |
|---|---|---|---|
| categories | string[] | いいえ | `element.search_elements` と同じ |
| classNames | string[] | いいえ | 例: `"Wall"`, `"Floor"`（ベストエフォート） |
| levelId | int | いいえ | ベストエフォート（要素種別で保存先が異なる） |
| name | object | いいえ | `mode: equals/contains/startsWith/endsWith/regex` |
| bbox | object | いいえ | `unit: mm/cm/m/ft/in`, `mode: intersects/inside` |
| parameters | object[] | いいえ | 初版は post-filter（後段評価） |

#### `filters.parameters[]`
- `param.kind`: `name` / `builtin` / `guid` / `paramId`
- `op`:
  - 文字列: `equals/contains/startsWith/endsWith`
  - 整数/ElementId: `equals/gt/gte/lt/lte/range`
  - double: `equals/gt/gte/lt/lte/range`（初版は **長さパラメータ前提の単位換算**）
- `range` は `min` と `max` が必要です。
- double比較は `unit`（mm/cm/m/ft/in）を指定できます（内部は長さ換算を前提）。

#### `options`
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| includeElementType | bool | いいえ | true |
| includeBoundingBox | bool | いいえ | false |
| includeParameters | string[] | いいえ | [] |
| maxResults | int | いいえ | 200（上限: 2000） |
| orderBy | string | いいえ | "id"（"id" or "name"） |

### リクエスト例（bbox + パラメータ）
```json
{
  "jsonrpc": "2.0",
  "id": "walls-A",
  "method": "element.query_elements",
  "params": {
    "scope": { "viewId": 12345, "includeHiddenInView": false },
    "filters": {
      "categories": ["Walls"],
      "levelId": 67890,
      "name": { "mode": "contains", "value": "Basic Wall", "caseSensitive": false },
      "bbox": {
        "min": { "x": 0, "y": 0, "z": 0, "unit": "mm" },
        "max": { "x": 10000, "y": 10000, "z": 3000, "unit": "mm" },
        "mode": "intersects"
      },
      "parameters": [
        { "param": { "kind": "name", "value": "Mark" }, "op": "contains", "value": "A-" }
      ]
    },
    "options": { "includeElementType": true, "includeBoundingBox": false, "maxResults": 200, "orderBy": "id" }
  }
}
```

## 戻り値
- `ok`: boolean
- `elements[]`: 要約（`id/uniqueId/name/category/className/levelId` + 任意で `typeId`/`bbox`/`parameters`）
- `diagnostics`:
  - `appliedFilters[]`
  - `counts`: `{ initial, afterQuickFilters, afterSlowFilters, returned }`
  - `warnings[]`（必要時）

## 備考
- `filters.levelId` は **ベストエフォート** です（要素種別でレベル情報の持ち方が異なるため）。
- `filters.parameters[]` は初版は post-filter です。性能面では `scope`/`categories`/`bbox` で候補を狭めるのが有効です。
- double型パラメータの比較は初版は「長さ」前提の換算です。長さ以外のdoubleには使わないでください。

