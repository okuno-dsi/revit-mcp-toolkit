# find_similar_by_factors

- カテゴリ: Rooms/Spaces
- 目的: 与えられた比較ファクター（レベル＋重心、カテゴリ）に類似する要素を現在のドキュメントから検索します。クロスポート／クロスモデル照合向け。

## 使い方
- メソッド: `find_similar_by_factors`

### パラメータ
- `categoryId`（推奨）: Revit カテゴリID（例: MEP スペース = -2008044）。未指定なら全カテゴリ対象。
- `level`（推奨）: レベル名。指定時は同一レベルの要素に限定。
- `centroid`（必須）: `{ "xMm": number, "yMm": number }` 平面重心（mm）。
- `maxDistanceMm`（任意）: 最大平面距離（mm）。既定なし。
- `top`（任意）: 返す件数上限。既定 5。

### リクエスト例（MEPモデルでスペースを検索）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "find_similar_by_factors",
  "params": {
    "categoryId": -2008044,
    "level": "4FL",
    "centroid": { "xMm": 12345.6, "yMm": 789.0 },
    "maxDistanceMm": 5000,
    "top": 5
  }
}
```

### 結果の形
```jsonc
{
  "ok": true,
  "result": {
    "items": [
      { "elementId": 6806612, "level": "4FL", "distanceMm": 120.4, "centroid": { "xMm": 12345.7, "yMm": 788.9 } }
    ]
  }
}
```

補足
- 可能な限り `categoryId` と `level` を指定すると高速かつ高精度になります。
- 距離はXY平面（mm）。
- 送信元モデルで `get_compare_factors` を実行し、その結果を本コマンドに渡す運用を想定しています。

## 関連
- get_compare_factors
- map_room_area_space
