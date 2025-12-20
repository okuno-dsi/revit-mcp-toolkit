# classify_wall_faces_by_side

- カテゴリ: Walls（壁）
- 目的: 壁要素ごとに各面を `exterior` / `interior` / `top` / `bottom` / `end` / `other` に分類し、必要に応じて幾何情報や Stable Reference を返します。

## 概要
1 つ以上の壁要素を解析し、各平面フェイスの「役割」を判定します。  
結果は `scan_paint_and_split_regions` や `apply_paint_by_reference` / `remove_paint_by_reference` と組み合わせることで、「外側の塗装だけ残す」「内側だけ消す」といった処理に利用できます。

## 使い方
- メソッド名: `classify_wall_faces_by_side`

### パラメータ
```jsonc
{
  "elementIds": [6799522, 6799523],  // 必須, 壁の ElementId 配列
  "offsetMm": 1000.0,                // 任意, 既定 1000.0
  "roomCheck": true,                 // 任意, 既定 true
  "minAreaM2": 1.0,                  // 任意, 既定 1.0
  "includeGeometryInfo": false,      // 任意, 既定 false
  "includeStableReference": true     // 任意, 既定 true
}
```

- `elementIds` (int[], 必須)  
  解析対象となる壁要素の `ElementId` の配列です。壁以外は `errors` にエラーとして返されます。
- `offsetMm` (double, 任意, 既定値 `1000.0`)  
  Room ベースで内外判定する際に、フェイス法線方向にオフセットする距離 (mm) です。  
  コマンド内部では:
  - `pOut = p + n * offsetFt`  
  - `pIn  = p - n * offsetFt`  
  の 2 点をサンプルし、`offsetFt` は mm からフィート（Revit 内部単位）へ変換されます。
- `roomCheck` (bool, 任意, 既定値 `true`)  
  `true` の場合、Room 内外判定を優先的に使って `interior` / `exterior` を判断します。  
  `false` の場合、`Wall.Orientation` や `HostObjectUtils.GetSideFaces` の情報のみで内外を推定します。
- `minAreaM2` (double, 任意, 既定値 `1.0`)  
  垂直フェイスを「側面」とみなすための最小面積 (m²) です。  
  これより小さい垂直フェイスは `end`（壁の端部）として分類されます。
- `includeGeometryInfo` (bool, 任意, 既定値 `false`)  
  `true` の場合、各フェイスに面積 (m²) と法線ベクトル `{x, y, z}` を含めて返します。
- `includeStableReference` (bool, 任意, 既定値 `true`)  
  `true` の場合、各フェイスに `stableReference`（安定参照文字列）を付与します。  
  これは `apply_paint_by_reference` や `get_face_paint_data` 等に渡すことができます。

### リクエスト例
```jsonc
{
  "jsonrpc": "2.0",
  "id": "classify-wall-faces",
  "method": "classify_wall_faces_by_side",
  "params": {
    "elementIds": [6799522],
    "offsetMm": 1000.0,
    "roomCheck": true,
    "minAreaM2": 1.0,
    "includeGeometryInfo": true,
    "includeStableReference": true
  }
}
```

## 戻り値
```jsonc
{
  "ok": true,
  "walls": [
    {
      "elementId": 6799522,
      "faces": [
        {
          "faceIndex": 0,
          "role": "exterior",
          "isVertical": true,
          "areaM2": 12.34,
          "normal": { "x": 0.0, "y": -1.0, "z": 0.0 },
          "stableReference": "abcd-efgh-..."
        }
      ]
    }
  ],
  "errors": []
}
```

- `ok` (bool): 全体として成功したかどうか。  
- `walls` (array): 正常に解析できた各壁要素ごとの結果。  
- `errors` (array): 壁ではない要素や、ジオメトリ取得失敗などのエラー情報。

各フェイス情報:
- `faceIndex`: 平面フェイスのインデックス。`get_wall_faces` のインデックスと整合するよう実装されています。
- `role`: 以下のいずれかの文字列:
  - `"exterior"`: 外側の垂直フェイス。
  - `"interior"`: 内側の垂直フェイス（Room 側）。
  - `"top"`: 上向きの水平フェイス。
  - `"bottom"`: 下向きの水平フェイス。
  - `"end"`: `minAreaM2` 未満の小さな垂直フェイス（壁厚端部など）。
  - `"other"`: 上記どれにも分類できない、または信頼性が低いフェイス。
- `isVertical`: 垂直フェイスなら `true`、水平フェイスなら `false`。
- `areaM2` (任意): `includeGeometryInfo == true` のときに返される面積 (m²)。
- `normal` (任意): `includeGeometryInfo == true` のときに返される単位法線ベクトル。
- `stableReference` (任意): `includeStableReference == true` のときに返される Stable Reference 文字列。

## 補足

- コマンド自体はモデルを変更しません（読み取り専用です）。
- 内外判定は、優先度として「Room 判定 > HostObjectUtils 情報 > 壁の Orientation」に基づきます。
- 非常に複雑な壁で端部フェイスが多い場合は、`minAreaM2` を大きめに設定すると整理しやすくなります。
- 以下のペイント系コマンドと組み合わせることを想定しています:
  - `scan_paint_and_split_regions`（塗装・Split Face のある要素を抽出）
  - `get_face_paint_data`（フェイスごとの塗装情報を取得）
  - `apply_paint_by_reference` / `remove_paint_by_reference`（Stable Reference を使った塗装/除去）

