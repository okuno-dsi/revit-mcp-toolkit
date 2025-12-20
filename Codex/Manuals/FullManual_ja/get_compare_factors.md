# get_compare_factors

- カテゴリ: Rooms/Spaces
- 目的: 要素の比較用ファクター（カテゴリ・レベル・重心・バウンディングボックス寸法）を mm で返し、モデル間（ポート間）マッチングに利用できるようにします。

## 使い方
- メソッド: `get_compare_factors`

### パラメータ
- `elementIds`（必須）: 評価する Revit 要素IDの配列。例: `[6809314]`

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_compare_factors",
  "params": { "elementIds": [6809314] }
}
```

### 結果の形
```jsonc
{
  "ok": true,
  "result": {
    "items": [
      {
        "elementId": 6809314,
        "categoryId": -2000160,
        "category": "Rooms",
        "levelId": 12345,
        "level": "4FL",
        "centroid": { "xMm": 12345.6, "yMm": 789.0, "zMm": 10200.0 },
        "bbox": { "dxMm": 12000.0, "dyMm": 8000.0, "dzMm": 2800.0 }
      }
    ]
  }
}
```

補足
- 部屋は外周境界から重心を算出、エリアは領域の重心、スペースは可能なら `Location` を用います。得られない場合はバウンディングボックス中心を使用します。
- 寸法はすべてミリメートルです。
- `find_similar_by_factors` と組み合わせて、図面・モデル間での要素照合に使います。

## 関連
- map_room_area_space
- find_similar_by_factors
- classify_points_in_room
