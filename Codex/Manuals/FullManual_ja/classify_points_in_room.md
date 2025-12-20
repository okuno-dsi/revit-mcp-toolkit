# classify_points_in_room

- カテゴリ: Spatial
- 目的: 指定した Room / Space に対して、複数の点 (X,Y,Z) が「室内」か「室外」かを分類します。

## 概要
このコマンドは、**点だけ** を入力として Room / Space の内外を判定する「軽量なテスト用コマンド」です。

- 判定対象は「点」であり、柱・壁などの **要素の高さ方向の広がり** は考慮しません。
- すべての点が室外だった場合は、要素ベースのコマンド（`get_spatial_context_for_element` / `get_spatial_context_for_elements`）を併用するように `messages` で案内されます。

## 使い方
- メソッド: classify_points_in_room

### パラメータ
| 名前 | 型 | 必須 | 説明 |
|---|---|---|---|
| roomId | int / string | はい | 判定対象の Room または Space の `ElementId` |
| points | number[][] | はい | 各点の座標 `[x_mm, y_mm, z_mm]` (mm 単位) の配列 |

- `roomId`  
  - `ElementId.IntegerValue` もしくはその文字列。  
  - `Autodesk.Revit.DB.Architecture.Room` または `Autodesk.Revit.DB.Mechanical.Space` が対象です。
- `points`  
  - mm 単位の座標配列です。例: `[[1000.0, 2000.0, 0.0], [1500.0, 2500.0, 0.0]]`

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "classify_points_in_room",
  "params": {
    "roomId": 6809314,
    "points": [
      [21961.201, 5686.485, 12300.0],
      [22000.0, 5700.0, 12300.0]
    ]
  }
}
```

## レスポンス

```jsonc
{
  "ok": true,
  "inside": [
    [21961.201, 5686.485, 12300.0]
  ],
  "outside": [
    [22000.0, 5700.0, 12300.0]
  ],
  "messages": [
    "指定された全ての点は Room/Space の外側と判定されました。柱や壁など要素として室内を通過しているかを調べる場合は、get_spatial_context_for_element / get_spatial_context_for_elements などの要素ベースのコマンドと併用してください。"
  ]
}
```

- `inside`  
  - 室内と判定された点の配列（入力と同じ形式 `[x_mm,y_mm,z_mm]`）。
- `outside`  
  - 室外と判定された点の配列。
- `messages`  
  - 追加の注意・案内メッセージ。  
  - `inside` が空のときには、要素ベースの解析コマンドへの誘導メッセージが含まれます。

## get_spatial_context_for_element(s) との違い

- `classify_points_in_room`  
  - 入力: Room/Space の `roomId` と点群 `points`。  
  - 出力: 各点が「その Room/Space 内かどうか」だけを返す。  
  - **要素の高さ方向（柱や壁の上下範囲）や BoundingBox は考慮しません。**

- `get_spatial_context_for_element` / `get_spatial_context_for_elements`  
  - 入力: 要素ID (`elementId` / `elementIds`)。  
  - 出力: 要素の代表点や曲線サンプル点について、Room / Space / Area のコンテキストを返します。  
  - 強化版では、要素の BoundingBox を用いて **縦方向の中間高さ** でも Room 判定を行うため、
    - 「柱の基準レベルオフセットが −100mm だが、途中で部屋を貫いている」ようなケースも検出できます。

### 推奨の使い分け

- 「この点が室内かどうかだけ知りたい」 → `classify_points_in_room`（軽量で高速）。  
- 「柱や壁が、部屋レベルをまたいで通過しているかも含めて知りたい」 →  
  - まず点でざっくり確認 (`classify_points_in_room`)  
  - すべて outside なら `get_spatial_context_for_element(s)` で要素ベースの詳細解析、という流れがおすすめです。

