# get_candidate_exterior_floors

- カテゴリ: Floors（床）
- 目的: 外壁近傍にあり、かつ床下に Room が存在しない床を「外部床候補」として一括取得します。  
  主に 1 階の外部スラブや外部デッキなどを想定しています。

## 概要
以下の手順で床を評価します。

1. `get_candidate_exterior_walls` を使って外壁候補となる壁を検出し、壁芯線（平面）を取得。
2. 各床について、バウンディングボックス内に格子状のサンプル点を取り、床上・床下それぞれで  
   「床または屋根に上下を挟まれた Room 内部ボリューム」に含まれるかどうかを判定します。
3. 「床上には Room がある」「床下には Room がない」かつ「外壁に近い」床を外部床候補として返します。

モデルは変更せず、外部床の絞り込みに使うための読み取り専用コマンドです。

## 使い方
- メソッド名: `get_candidate_exterior_floors`

### パラメータ
```jsonc
{
  "viewId": 123456,                        // 任意, int
  "levelId": 200001,                       // 任意, int
  "levelIds": [200001, 200002],            // 任意, int[]
  "maxDistanceFromExteriorWallMm": 1500.0, // 任意, double (既定 1500.0)
  "minExteriorWallAreaM2": 1.0,            // 任意, double (既定 1.0)
  "sampleGrid": 3,                         // 任意, int (1..7, 既定 3)
  "outsideThreshold": 0.5,                 // 任意, double 0..1 (既定 0.5)
  "useRooms": true                         // 任意, bool (既定 true)
}
```

- `viewId` (int, 任意)  
  指定した場合、そのビューに見えている床・壁のみを対象とします。
- `levelId` (int, 任意)  
  単一レベルでの絞り込みです。`LevelId` がこの ID の床のみ対象になります。
- `levelIds` (int[], 任意)  
  複数レベルでの絞り込みです。配列が空でなければ `levelId` より優先されます。
- `maxDistanceFromExteriorWallMm` (double, 任意, 既定値 `1500.0`)  
  床のバウンディングボックス中心から外壁候補の壁芯線までの XY 平面上の最短距離 (mm) が、この値以下の床のみを対象とします。
- `minExteriorWallAreaM2` (double, 任意, 既定値 `1.0`)  
  外壁候補とみなす壁の「外側フェイス面積合計」(m²) の最低値です。  
  これにより、極小の壁を外壁候補から除外します。
- `sampleGrid` (int, 任意, 既定値 `3`, 許容範囲 1..7)  
  床ごとにバウンディングボックス内で `sampleGrid x sampleGrid` 個のサンプル点を取ります。
- `outsideThreshold` (double, 任意, 既定値 `0.5`, 範囲 0..1)  
  「床下に Room が無い」とみなすためのしきい値です。  
  床下の Room 率（`roomBelowRatio`）が十分低い場合に「外部」と判定します。
- `useRooms` (bool, 任意, 既定値 `true`)  
  `true` の場合、Room を使って上下の内外判定を行います。`false` の場合は Room 判定を行わず、すべて床下に Room が無い前提となります。

### 判定ロジック（概要）

各床について:

- サンプル点を床上（少し上）と床下（少し下または下階レベル寄り）に配置し、
  - `roomAboveRatio` = 床上サンプルのうち、室内ボリューム内にある点の割合
  - `roomBelowRatio` = 床下サンプルのうち、室内ボリューム内にある点の割合
- レベル構造を見て、その床が「最下レベルかどうか」も判定します。

外部床候補とみなす条件（簡略化）は:

- 床上に Room がある: `roomAboveRatio >= 0.5`  
- 床下に Room がない:
  - 最下レベルの場合: `roomBelowRatio == 0.0`
  - それ以外の場合: `roomBelowRatio` が `outsideThreshold` に応じて十分小さい
- かつ、`maxDistanceFromExteriorWallMm` 以内に外壁候補の壁芯線が存在すること。

## 戻り値
```jsonc
{
  "ok": true,
  "msg": null,
  "totalCount": 5,
  "floors": [
    {
      "elementId": 61230001,
      "id": 61230001,
      "uniqueId": "f8f6455a-...",
      "levelId": 60521762,
      "typeId": 54010001,
      "typeName": "EXT-SLAB-150",
      "distanceFromExteriorWallMm": 420.5,
      "roomAboveRatio": 1.0,
      "roomBelowRatio": 0.0,
      "isLowestLevel": true
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
- `msg` (string|null): 補足メッセージ。外壁候補が見つからない場合などに文字列が入ります。
- `totalCount` (int): `floors` に含まれる外部床候補の件数。
- `floors` (array): 外部床候補の床一覧。
  - `elementId` / `id`: 床要素の `ElementId`。
  - `uniqueId`: Revit の `UniqueId` 文字列。
  - `levelId`: 対応レベルの `ElementId`（不明な場合は 0）。
  - `typeId`: 床タイプの `ElementId`。
  - `typeName`: 床タイプ名。
  - `distanceFromExteriorWallMm`: 外壁候補の壁芯線までの最短距離 (mm)。
  - `roomAboveRatio`: 床上サンプルのうち Room 内にある割合。
  - `roomBelowRatio`: 床下サンプルのうち Room 内にある割合。
  - `isLowestLevel`: この床のレベルより下にレベルが無い場合 `true`。

## 補足

- 外壁候補の抽出は `get_candidate_exterior_walls` に委ねられており、そちらのコマンドと同様に、
  壁の基準線サンプリングと Room 情報にもとづく概略的な判定ロジックです。
- Room 判定では、床上・床下のサンプル点が「床または屋根に上下を挟まれた Room 内部ボリューム」に含まれるかどうかで判断し、
  Level/BaseOffset/UpperLimit/UnboundedHeight などから上下範囲を推定しています。
- いずれも **概略手法** であり、凹凸の多いファサードや大きな吹き抜け、Room が配置されていない領域が多い建物では、
  誤判定（内部と外部の取り違え）が生じやすくなります。
- 高い精度が必要な用途では、本コマンドの結果を直接確定値とはせず、ビュー上の色分けやレポートで可視化しつつ、
  設計者の知識やタイプ名などと合わせて確認・補正することを推奨します。
- より詳細な解析や塗装・塗りつぶし処理を行いたい場合は、次のコマンドと組み合わせて使用できます:
  - `get_candidate_exterior_walls`
  - `get_candidate_exterior_columns`
  - `scan_paint_and_split_regions`
  - `get_face_paint_data`
  - `create_filled_region`, `apply_paint_by_reference`, `remove_paint_by_reference`
