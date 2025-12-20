# get_candidate_exterior_columns

- カテゴリ: Columns（柱）
- 目的: 外壁近傍にある柱（構造柱 / 建築柱）を「外周柱候補」として一括取得します。  
  判定は、柱の位置と外壁候補の壁芯線との XY 平面上の最短距離にもとづきます。

## 概要
ビューおよびレベルで絞り込んだ柱を走査し、  
外壁候補とみなされる壁の芯線からの水平方向距離を計算します。  
距離がしきい値以下の柱を「外周柱候補」として返します。

このコマンド自体はモデルを変更しない読み取り専用コマンドで、代表的な用途は:
- 「建物外周部にある柱だけリストアップしたい」
- 「外周柱だけを対象に塗装・仕上・集計処理を行いたい」

## 使い方
- メソッド名: `get_candidate_exterior_columns`

### パラメータ
```jsonc
{
  "viewId": 123456,                     // 任意, int
  "levelId": 200001,                    // 任意, int
  "levelIds": [200001, 200002],         // 任意, int[]
  "maxDistanceMm": 1000.0,              // 任意, double (既定 1000.0)
  "minExteriorWallAreaM2": 1.0,         // 任意, double (既定 1.0)
  "includeArchitecturalColumns": true,  // 任意, bool (既定 true)
  "includeStructuralColumns": true      // 任意, bool (既定 true)
}
```

- `viewId` (int, 任意)  
  指定した場合、そのビューに見えている柱・壁のみを対象とします。
- `levelId` (int, 任意)  
  単一レベルでの絞り込みです。`LevelId` がこの ID の柱のみ対象になります。
- `levelIds` (int[], 任意)  
  複数レベルでの絞り込みです。配列が空でなければ `levelId` より優先されます。
- `maxDistanceMm` (double, 任意, 既定値 `1000.0`)  
  柱の位置から外壁候補の壁芯線までの XY 平面上の最短距離 (mm) が、この値以下のものを外周柱候補とみなします。
- `minExteriorWallAreaM2` (double, 任意, 既定値 `1.0`)  
  外壁候補とみなすための壁の「外側フェイス面積合計」(m²) の最低値です。  
  `get_candidate_exterior_walls` と同じ考え方で、外壁になり得ない小さな壁を除外するためのパラメータです。
- `includeArchitecturalColumns` (bool, 任意, 既定値 `true`)  
  `true` の場合、建築柱 (`OST_Columns`) を対象に含めます。
- `includeStructuralColumns` (bool, 任意, 既定値 `true`)  
  `true` の場合、構造柱 (`OST_StructuralColumns`) を対象に含めます。

## 戻り値
```jsonc
{
  "ok": true,
  "msg": null,
  "totalCount": 8,
  "columns": [
    {
      "elementId": 61234567,
      "id": 61234567,
      "uniqueId": "f8f6455a-...",
      "levelId": 60521762,
      "category": "StructuralColumns",
      "typeId": 54000001,
      "typeName": "C-600x600",
      "distanceMm": 350.0
    }
  ],
  "inputUnits": {
    "Length": "mm",
    "Area": "m2",
    "Volume": "m3",
    "Angle": "deg"
  },
  "internalUnits": {
    "Length": "ft",
    "Area": "ft2",
    "Volume": "ft3",
    "Angle": "rad"
  }
}
```

- `ok` (bool): コマンド全体として成功したかどうか。  
- `msg` (string|null): 補足メッセージ。外壁候補が見つからない場合などに文字列が入ることがあります。
- `totalCount` (int): `columns` に含まれる外周柱候補の件数。
- `columns` (array): 外周柱候補の柱一覧。
  - `elementId` / `id`: 柱要素の `ElementId`。
  - `uniqueId`: Revit の `UniqueId` 文字列。
  - `levelId`: 柱の `LevelId`（不明な場合は 0）。
  - `category`: `"StructuralColumns"` または `"Columns"`。
  - `typeId`: 柱タイプの `ElementId`。
  - `typeName`: 柱タイプ名。
  - `distanceMm`: 外壁候補の壁芯線までの最短距離 (mm)。

## 補足

- 外壁候補の壁の抽出は、内部的には `get_candidate_exterior_walls` と同等のロジックに基づいており、  
  壁の基準線サンプリングと Room 情報を用いた概略的な判定の結果に依存します。  
  ここで用いる外側面積も近似値であり、「外壁候補のあたりを付ける」ための指標です。
- 柱の位置は `LocationPoint` を優先し、取得できない場合はバウンディングボックスの中心点を使用します。
- 距離の計算は XY 平面上の点と線分（壁芯線）との最短距離です。  
  壁厚分だけ実際の仕上位置とはずれますし、凹凸の多い外周形状や中庭・下屋・ピロティが入り組んだ建物では、  
  「外部柱／内部柱」の取り違えが生じ得ます。
- このコマンドも外壁・外部床と同様に、「外周柱候補をざっくり抽出するためのヒューリスティック」と位置付けてください。  
  重要な用途では、結果をビュー上で色分け表示し、モデルを見ながら人間が確認・補正することを推奨します。
- より詳細な解析や塗装処理を行いたい場合は、次のコマンドと組み合わせて使うことを想定しています:
  - `get_candidate_exterior_walls`
  - `classify_wall_faces_by_side`
  - `get_wall_faces`, `get_face_paint_data`
  - `apply_paint_by_reference`, `remove_paint_by_reference`
