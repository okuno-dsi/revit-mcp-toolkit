# set_project_base_point

- カテゴリ: SiteOps
- 目的: プロジェクト基点を設定します。

## 概要
モデル形状は動かさず、基準座標だけを移動します（シート/ビューの見た目は変わらず、座標表示だけが変わります）。
意匠・構造で原点がズレている場合の調整に使用できます。

## 使い方
- Method: set_project_base_point

### パラメータ
| 名前 | 型 | 必須 | デフォルト |
|---|---|---|---|
| xyzMm | object {x,y,z} | いいえ |  |
| deltaMm | object {x,y,z} | いいえ |  |
| fromPointMm | object {x,y,z} | いいえ |  |
| toPointMm | object {x,y,z} | いいえ |  |
| angleToTrueNorthDeg | number | いいえ | 0.0 |
| sharedSiteName | string | いいえ |  |

**座標指定ルール**
- `xyzMm`: 絶対座標で基点を設定
- `deltaMm`: 現在位置からの移動量
- `fromPointMm` + `toPointMm`: `deltaMm = to - from` を計算して移動
- `xyzMm/deltaMm/fromPointMm/toPointMm` が無い場合は角度のみ更新

### 例（絶対座標）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_project_base_point",
  "params": {
    "xyzMm": {"x": 1000, "y": -500, "z": 0},
    "angleToTrueNorthDeg": 0.0
  }
}
```

### 例（移動量）
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "set_project_base_point",
  "params": {
    "deltaMm": {"x": -250, "y": 0, "z": 0}
  }
}
```

### 例（from/to）
```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "set_project_base_point",
  "params": {
    "fromPointMm": {"x": 10000, "y": 20000, "z": 0},
    "toPointMm": {"x": 0, "y": 0, "z": 0}
  }
}
```

## 関連
- set_survey_point
- set_shared_coordinates_from_points
- get_site_overview
