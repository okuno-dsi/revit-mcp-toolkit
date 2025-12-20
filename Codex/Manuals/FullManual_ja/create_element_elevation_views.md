# create_element_elevation_views

- カテゴリ: ViewOps
- 目的: 任意の要素に対して、正面（あるいは任意方向）からの立面／断面ビューを自動生成し、必要に応じて要素のみが表示されるビューを作成します。

## 概要
このコマンドは JSON-RPC を通じて Revit MCP アドインに対して実行されます。`elementIds` で指定した要素ごとにビュー方向を決め、ElevationMarker または SectionBox を用いて要素にフォーカスしたビューを作成します。既定では、ビュー内で対象要素以外を非表示にし、レビューや資料作成をしやすくします。

## 使い方
- メソッド: create_element_elevation_views

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| elementIds | int[] | はい |  |
| mode | string | いいえ | SectionBox |
| orientation.mode | string | いいえ | Front |
| orientation.customDirection.x/y/z | number | いいえ |  |
| isolateTargets | bool | いいえ | true |
| viewTemplateName | string | いいえ |  |
| viewFamilyTypeName | string | いいえ | MCP_Elevation（なければ Elevation/Section を自動選択） |
| viewScale | int | いいえ | 50 |
| cropMargin_mm | number | いいえ | 200 |
| offsetDistance_mm | number | いいえ | 1500 |

補足:
- `mode`:
  - `"SectionBox"`（既定）: `ViewSection.CreateSection` とカスタム `BoundingBoxXYZ` を試し、失敗した場合は ElevationMarker 方式にフォールバックします。
  - `"ElevationMarker"`: 常に `ElevationMarker.CreateElevation` を使用します（ドア／窓の専用トリミングロジックもこちらで適用されます）。
- `orientation.mode`:
  - `"Front"`（既定）, `"Back"`, `"Left"`, `"Right"`, `"CustomVector"`。
  - `"CustomVector"` の場合は `orientation.customDirection` (x,y,z) に有効なベクトルが必要です。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_element_elevation_views",
  "params": {
    "elementIds": [60576259, 60580001],
    "mode": "ElevationMarker",
    "orientation": {
      "mode": "Front"
    },
    "isolateTargets": true,
    "viewFamilyTypeName": "MCP_Elevation",
    "viewScale": 50,
    "cropMargin_mm": 200,
    "offsetDistance_mm": 1500
  }
}
```

### 結果例
```jsonc
{
  "ok": true,
  "msg": "Created elevation views for 2 element(s).",
  "views": [
    {
      "elementId": 60576259,
      "viewId": 61000001,
      "viewName": "Element_60576259_Elev",
      "mode": "ElevationMarker"
    },
    {
      "elementId": 60580001,
      "viewId": 61000002,
      "viewName": "Element_60580001_Elev",
      "mode": "ElevationMarker"
    }
  ],
  "skippedElements": []
}
```

一部の要素が処理できない場合（例: 有効な向き情報がなく、`CustomVector` も指定されていない場合）は、`skippedElements` に理由付きで出力されます。

```jsonc
{
  "ok": true,
  "msg": "Created elevation views for 1 element(s).",
  "views": [ /* ... */ ],
  "skippedElements": [
    {
      "elementId": 60590001,
      "reason": "有効な向きベクトルを決定できませんでした。"
    }
  ]
}
```

## 補足
- `viewFamilyTypeName` で名前一致する `ViewFamilyType` が見つからない場合、`mode` に応じて Elevation もしくは Section の既定タイプを自動的に検索します。
- ビュー名:
  - 実際に使用された方式（レスポンスの `mode`）が `"ElevationMarker"` の場合: `Element_<elementId>_Elev` を基準にユニーク名を生成します。
  - 実際に使用された方式（レスポンスの `mode`）が `"SectionBox"` の場合: `Element_<elementId>_Sec` を基準にユニーク名を生成します。
- `orientation.mode` が `CustomVector` 以外で、要素が FacingOrientation/HandOrientation を持たないなど向き情報が得られない場合は、グローバル Y+ 方向 `{0,1,0}` を Front とみなします。向きの解決に失敗した要素は `skippedElements` に理由付きで出力されます。
- `orientation.doorViewSide`（任意, `"Exterior"` / `"Interior"`）を指定すると、ドアについて室内側／室外側のどちらから見るかを自動判定して向きを補正します。既定は `"Exterior"`（外側から室内を見る）です。
- `depthMode`（任意, `"Auto"` / `"DoorWidthPlusMargin"`）を指定すると、ビューの奥行きの扱いを制御できます。`"DoorWidthPlusMargin"` はドア／窓／壁／カーテンウォールに対して要素厚＋マージンまでに奥行きを絞るモードです。
- ドア／窓（およびカーテンウォールパネル）については、タイプ寸法（幅・高さ）と要素ジオメトリ中心から、ビュー平面上で左右上下が揃うようにクロップ範囲を計算します。一般要素については従来どおり `get_BoundingBox` の投影＋マージンでトリミングします。
- `isolateTargets` が true の場合、新しいビュー内で可視な要素を収集し、指定された `elementIds` 以外を `HideElements` で非表示にすることで、常に対象要素だけが表示された状態になります。

## 関連コマンド
- create_section
- create_elevation_view
- get_oriented_bbox
