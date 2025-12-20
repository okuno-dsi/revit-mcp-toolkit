# get_area_metrics

- カテゴリ: Area
- 目的: Area 要素の面積(m²)と周長(mm)を取得します（単体/複数）。

## 使い方
- メソッド: get_area_metrics

### パラメータ（単体）
| 名前 | 型 | 必須 | 既定値 |
|---|---:|:---:|---|
| areaId | int | はい |  |
| boundaryLocation | string | いいえ | `Finish` |

### パラメータ（複数）
| 名前 | 型 | 必須 | 既定値 |
|---|---:|:---:|---|
| areaIds | int[] | はい |  |
| startIndex | int | いいえ | 0 |
| batchSize | int | いいえ | 200 |
| maxMillisPerCall | int | いいえ | 0（制限なし） |
| boundaryLocation | string | いいえ | `Finish` |

補足:
- 周長は `GetBoundarySegments(...)` を `boundaryLocation` で計算します（A: 計算境界）。`Manuals/FullManual_ja/spatial_boundary_location.md` 参照。

## リクエスト例（単体）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_area_metrics",
  "params": { "areaId": 11122204, "boundaryLocation": "Finish" }
}
```

## リクエスト例（複数）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_area_metrics",
  "params": {
    "areaIds": [11122204, 11122242, 11122243],
    "startIndex": 0,
    "batchSize": 200,
    "maxMillisPerCall": 0,
    "boundaryLocation": "Finish"
  }
}
```

## 結果
### 単体
- `elementId`, `areaM2`, `perimeterMm`, `level`, `boundaryLocation`

### 複数
- `items`: `{ elementId, areaM2, perimeterMm, level }` の配列
- `completed` / `nextIndex`: ページング用
- トップレベルに `boundaryLocation` が含まれます。
