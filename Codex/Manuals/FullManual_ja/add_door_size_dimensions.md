# add_door_size_dimensions

- Category: AnnotationOps
- 目的: ビューにドアの幅/高さの寸法注記（関連寸法）を追加します（主に立面/断面ビュー向け）。

## 概要
ドアサイズ（幅・高さ）が分かるように、Revit の `Dimension` を自動作成します。

- 対象はドアの `FamilyInstance` です。
- `FamilyInstanceReferenceType` の参照（幅=`Left/Right`、高さ=`Bottom/Top`）を使うため、**ドアに関連付いた寸法**になります（ドア寸法変更に追従）。
- 寸法線の位置は、ビュー座標系に投影したドアの BoundingBox から算出し、`offsetMm` だけ外側にオフセットします。
- 複数オフセット（多重寸法）や、任意の参照ペアによる追加寸法（`dimensionSpecs`）に対応します。

## 使い方
- Method: `add_door_size_dimensions`

### パラメータ
```jsonc
{
  "viewId": 0,                    // 任意（省略時: アクティブビュー）
  "doorIds": [123],               // 任意（elementIds でも可）。省略時はビュー内で見えているドア全て
  "addWidth": true,               // 任意（既定: true）
  "addHeight": true,              // 任意（既定: true）

  // オフセット(mm)。配列指定で多重寸法を作成します。
  "offsetMm": 200.0,              // 任意（既定: 200）フォールバックのオフセット
  "widthOffsetMm": 200.0,         // 任意 幅用の単一オフセット
  "heightOffsetMm": 200.0,        // 任意 高さ用の単一オフセット
  "widthOffsetsMm": [150, 300],   // 任意 幅の多重オフセット（優先）
  "heightOffsetsMm": [150, 300],  // 任意 高さの多重オフセット（優先）

  // オフセットの挙動
  // - absolute  : offsetMm をそのまま「モデル空間での距離」として使う（従来）
  // - leaderPlus: 最終オフセット = (leaderLengthPaperMm * view.Scale) + offsetMm
  "offsetMode": "leaderPlus",      // 任意 "leaderPlus"|"absolute"（既定: "leaderPlus"）
  "leaderLengthPaperMm": null,     // 任意 leaderPlus のベース長さ（紙面mm）の上書き

  // 配置
  "widthSide": "top",             // 任意 "top"|"bottom"（既定: "top"）
  "heightSide": "left",           // 任意 "left"|"right"（既定: "left"）

  // 寸法タイプ（任意。typeId または typeName を指定）
  "typeId": 0,                    // 任意 DimensionType の id（0 なら既定のまま）
  "typeName": null,               // 任意 DimensionType の名前（大文字小文字は無視した完全一致）

  // ビューテンプレート/表示の安全策
  "detachViewTemplate": false,    // 任意（既定: false）
  "ensureDimensionsVisible": true,// 任意（既定: true）寸法カテゴリが非表示なら表示に変更を試みます
  "keepInsideCrop": true,         // 任意（既定: true）CropBox 内に収めるよう寸法線をクランプ
  "expandCropToFit": true,        // 任意（既定: keepInsideCrop と同じ）クランプせず CropBox を拡張して距離を確保
  "cropMarginMm": 30.0,           // 任意（既定: 30）CropBox 端からの余白

  // 作成した寸法へのオーバーライド（任意。投影線色）
  "overrideRgb": { "r": 255, "g": 0, "b": 0 },

  // 高度な使い方: 任意の参照ペアで寸法を追加（dimensionSpecs）
  // refA/refB は以下の形式をサポートします:
  //  - stable文字列: "...."
  //  - object: { "stable":"...." } または { "refType":"Left", "index":0 }
  "dimensionSpecs": [
    {
      "enabled": true,
      "name": "custom_1",
      "orientation": "horizontal", // "horizontal"|"vertical"
      "side": "top",               // horizontal: "top"|"bottom" / vertical: "left"|"right"
      "offsetMm": 400,
      "refA": "STABLE_REF_A",
      "refB": "STABLE_REF_B",
      "typeId": 0,
      "typeName": null
    }
  ],

  "debug": false                  // 任意（既定: false）BBox 等のデバッグ情報を含める
}
```

### リクエスト例（アクティブビュー）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "add_door_size_dimensions",
  "params": {
    "offsetMm": 200,
    "addWidth": true,
    "addHeight": true
  }
}
```

## 注意点
- ドアファミリ側に `Left/Right` や `Bottom/Top` の参照が無い場合、その方向の寸法はスキップされます（`skipped` に理由が入ります）。
- `ensureDimensionsVisible` で表示変更が必要でも、ビューテンプレートがロックしている場合は `ok:false` / `code:"VIEW_TEMPLATE_LOCK"` で返します（`detachViewTemplate:true` か手動でテンプレート解除）。
- 既定の `offsetMode:"leaderPlus"` では、寸法タイプから（可能な範囲で）紙面のベース長さを解決し、それを `view.Scale` でモデル空間に換算した分を `offsetMm` に加算します。タイトな要素ビューでも寸法を外側に出しやすくなります。
- ガラス/ガラリ/ノブ位置など、外形以外の寸法を取りたい場合は、先に `get_family_instance_references` で stable 参照を取得し、`dimensionSpecs` で指定してください。
