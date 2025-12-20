# create_filled_region

- カテゴリ: Annotations/FilledRegion
- 用途: mm 座標で指定したポリゴンから、ビュー内に FilledRegion（塗りつぶし領域）を作成します。

## 概要
1 つ以上のポリゴンループ（mm 単位の頂点列）を指定して、対象ビューに `FilledRegion` を作成します。  
色やパターンは `FilledRegionType` 側で管理されるため、事前に `get_filled_region_types` で利用可能なタイプを確認しておくと便利です。

## 使い方
- メソッド: `create_filled_region`

### パラメータ
```jsonc
{
  "viewId": 11120738,
  "typeId": 12345,              // 任意。省略時は既定の FilledRegionType
  "foregroundPatternId": 2001,  // 任意: 前景パターンの ElementId
  "foregroundPatternName": "Solid fill",      // 任意: 前景パターン名（ID未指定時に使用）
  "backgroundPatternId": 2002,  // 任意: 背景パターンの ElementId
  "backgroundPatternName": "Diagonal up",     // 任意: 背景パターン名
  "loops": [
    [
      { "x": 0, "y": 0, "z": 0 },
      { "x": 1000, "y": 0, "z": 0 },
      { "x": 1000, "y": 500, "z": 0 },
      { "x": 0, "y": 500, "z": 0 }
    ]
  ]
}
```

- `viewId` (int, 必須)
  - 作成先ビューの ID。テンプレートビューは使用できません。
- `typeId` (int, 任意)
  - 使用する `FilledRegionType` の ID。省略時は Revit の既定タイプが使われます。
- `foregroundPatternId` / `foregroundPatternName` (任意)
  - 前景パターンとして使用する `FillPatternElement` を ID または名前で指定します。
  - 指定された場合、ベースの FilledRegionType を複製し、その前景パターンだけ上書きした専用タイプを内部で作成して使用します。
- `backgroundPatternId` / `backgroundPatternName` (任意)
  - 背景パターンについても同様です。
- `loops` (配列, 必須)
  - ポリゴンループの配列。各ループは `{x,y,z}`（mm）の頂点配列です。
  - 3 点以上の頂点が必要で、コマンド側で自動的に始点と終点を閉じます。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": "region-rect",
  "method": "create_filled_region",
  "params": {
    "viewId": 11120738,
    "typeId": 12345,
    "loops": [
      [
        { "x": 0, "y": 0, "z": 0 },
        { "x": 1000, "y": 0, "z": 0 },
        { "x": 1000, "y": 500, "z": 0 },
        { "x": 0, "y": 500, "z": 0 }
      ]
    ]
  }
}
```

## 戻り値
```jsonc
{
  "ok": true,
  "elementId": 600001,
  "uniqueId": "....",
  "typeId": 12345,
  "viewId": 11120738
}
```

## 補足

- 座標は mm で指定し、内部で Revit 内部単位（ft）に変換されます。
- 平面図ビューでは通常 `z = 0` 付近を指定しますが、ビューの切断面と整合していれば問題ありません。
- 穴あき領域を表現したい場合は、外側ループ＋内側ループを複数指定できますが、Revit 側の制約（ループの向きなど）により失敗する場合があります。
- パターンを指定した場合、ベースとなる FilledRegionType を `_McpFill` というサフィックス付きの名前で複製し、
  その複製に前景/背景パターンを適用してから塗りつぶし領域を作成します。

## 関連コマンド
- `get_filled_region_types`
- `get_filled_regions_in_view`
- `replace_filled_region_boundary`
