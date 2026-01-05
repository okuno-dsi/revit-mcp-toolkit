# get_spatial_context_for_element

- カテゴリ: Spatial
- 目的: 指定した要素が、どの Room / Space / Zone / Area に属しているかをまとめて取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、1つの要素について「空間コンテキスト」を返します。  
要素が属する部屋 (Room)、スペース (MEP Space)、ゾーン (Zone)、エリア (Area) と、その AreaScheme を一括で確認できます。

- `elementId` は **必須** です（壁・床・機器・タグなど任意の要素を指定）。
- `include` を省略した場合、Room / Space / Zone / Area / AreaScheme を **すべて** 対象とします。
- 対象要素自体が Space や Area の場合は、代表点からヒットしなくても、その Space / Area 自身を必ず結果に含めます。

## 使い方
- メソッド: get_spatial_context_for_element

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| elementId | int | はい | - |
| phaseName | string | いいえ |  | 
| mode | string | いいえ | `"3d"` |
| include | string[] | いいえ | (省略時は全て) |
| bboxFootprintProbe | bool | いいえ | `true` |

- `elementId`  
  - 対象とする要素の `ElementId.IntegerValue`。
  - 何も指定しない場合はエラーになります。選択要素を使いたい場合は、先に `get_selected_element_ids` で ID を取得してください。
- `phaseName`  
  - Room / Space の検索に使うフェーズ名（省略時は Revit の最終フェーズに委ねます）。
- `mode`  
  - `"3d"`: XY+Z を考慮した位置（既定）。  
  - `"2d"`: XY のみを重視する簡易モード（主にデバッグ・理論検討向け）。
- `include`  
  - `"room"`, `"space"`, `"zone"`, `"area"`, `"areaScheme"` のいずれかを指定できます。
  - 省略時は、これらすべてを含めて検索します。
- `bboxFootprintProbe`
  - `true`（既定）の場合、代表点にヒットしないときに要素BBoxフットプリント（中間高さの角/辺中点）でも Room 判定を試みます。
  - `false` にすると BBox フットプリント判定を無効化します（厳密になりますが境界跨ぎ要素を取りこぼすことがあります）。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_spatial_context_for_element",
  "params": {
    "elementId": 60576295,
    "phaseName": "新築",
    "mode": "3d",
    "include": ["room", "space", "zone", "area", "areaScheme"]
  }
}
```

## レスポンス（成功時）
代表的なレスポンスの形は次のとおりです（フィールドは一部省略）。

```jsonc
{
  "ok": true,
  "elementId": 60576295,
  "referencePoint": { "x": 5038.119, "y": 20415.052, "z": 0.0 },
  "room": {
    "id": 60535031,
    "name": "倉庫A 32",
    "number": "32",
    "phase": "",
    "levelName": "1FL"
  },
  "spaces": [
    {
      "id": 60576295,
      "name": "スペース 7",
      "number": "7",
      "phase": "",
      "levelName": "1FL",
      "zone": {
        "id": 635618,
        "name": "既定値"
      }
    }
  ],
  "areas": [
    {
      "id": 60576279,
      "name": "面積 7",
      "number": "7",
      "areaScheme": { "id": 9490, "name": "07 建面" }
    }
  ],
  "areaSchemes": [
    { "id": 9490, "name": "07 建面" }
  ],
  "messages": [
    "Room was resolved using the final project phase.",
    "Found 1 Space(s) at the reference point.",
    "同レベルの Area による包含は見つかりませんでした（BoundingBox ベースの近似判定）。",
    "Area 判定: 代表点からのヒットはありませんでしたが、要素自身が Area なのでコンテキストに含めました。"
  ]
}
```

### 挙動のポイント
- `referencePoint`  
  - 要素の LocationPoint / LocationCurve の中点 / BoundingBox 中心などから決めた代表点（mm）。
- `room`  
  - Room は best effort で 1 件返します（代表点に加え、要素BBoxの中間高さでの判定も試行）。
  - 代表点のXYが室外でも要素BBoxが境界に掛かる場合は、BBoxの角/辺中点（中間高さ）でも判定して Room を見つけます。
- `spaces`  
  - 代表点を含む Space を列挙します。  
  - 代表点にヒットしなくても、対象要素自体が Space の場合は、その Space を 1 件必ず含めます。
- `areas` / `areaSchemes`  
  - 同じレベル上の Area の BoundingBox に代表点の XY が入っているものを近似的に検出します。  
  - 代表点にヒットしなくても、対象要素自体が Area の場合は、その Area とその AreaScheme を必ず含めます。
- `messages`  
  - 使用したフェーズや、ヒット数、近似判定であることなどの補足情報を配列で返します。

## 関連コマンド
- get_rooms
- get_spaces
- get_areas
- map_room_area_space
*** End Patch ***!
