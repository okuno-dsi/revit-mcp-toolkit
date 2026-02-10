# get_face_regions / get_face_region_detail / get_face_region_takeoff 手順書

この文書は、Revit アドイン（MCP）に対して JSON-RPC で以下の3コマンドを実行し、床や壁などの要素フェイス上にある「分割面（リージョン）」の情報・詳細・数量を取得する手順をまとめたものです。

- get_face_regions
- get_face_region_detail
- get_face_region_takeoff

## 前提
- Revit 側アドインに接続可能: `http://localhost:5210`
- 本リポジトリの `Scripts/Reference/send_revit_command_durable.py` を使用
- 例は Python 実行。Windows PowerShell での実行を想定

```bash
python Scripts/Reference/send_revit_command_durable.py --port 5210 --command <method> --params '{...}' [--output-file <path>]
```

## フェイスの特定（faceIndex）
リージョン取得前に、対象フェイスの index（`faceIndex`）を把握します。
- おすすめ: `get_paint_info` を `includeUnpainted=true` で実行し、返却される `faceIndex` 一覧と `faceStableReference` を確認

例（要素ID 61124693 の床の全フェイス）:
```bash
python Scripts/Reference/send_revit_command_durable.py --port 5210 --command get_paint_info \
  --params '{"elementId":61124693,"includeUnpainted":true}' \
  --output-file data/floor_61124693_paint_info.json
```
この例では、トップ面は `faceIndex: 1` でした。

---

## 1) get_face_regions（リージョン一覧／境界）
対象フェイス上の分割面（リージョン）一覧を取得します。必要に応じて境界ポリラインやメッシュを含められます。

- 主なパラメータ（抜粋）
  - `elementId` または `uniqueId`
  - `faceIndex`: 親フェイスのインデックス
  - `includeGeometry`（bool）: 境界ポリライン（`boundaryLoops`）を含めるか（既定 true）
  - `includeMesh`（bool）: 三角メッシュを含めるか（既定 false）
  - 出力量制御:
    - `tessellateChordMm`（数値, 例: 50〜2000）: 折れ線分解の最大Chord長[mm]
    - `simplifyToleranceMm`（数値, 例: 0/100/500）: Douglas–Peucker 簡略化許容[mm]
    - `maxPointsPerLoop`, `maxTotalPoints`, `regionLimit`

- 返却（概要）
  - `elementId`, `uniqueId`, `faceIndex`, `regionCount`
  - `regions`[]: `index`, `isPainted`, `material{id,name,color{hex,r,g,b}}`, `area{internalValue(ft2),m2}`, `plane`, `boundaryLoops`（座標は mm 系）, `mesh`（任意） ほか

- 実行例（床ID=61124693, トップ面=faceIndex 1, 境界あり・メッシュなし）
```bash
python Scripts/Reference/send_revit_command_durable.py --port 5210 --command get_face_regions \
  --params '{
    "elementId":61124693,
    "faceIndex":1,
    "includeGeometry":true,
    "includeMesh":false,
    "tessellateChordMm":1000,
    "simplifyToleranceMm":500,
    "maxPointsPerLoop":400,
    "maxTotalPoints":2000,
    "regionLimit":3
  }' \
  --output-file data/floor_61124693_face1_regions_with_geometry.json
```

- メモ
  - 処理が重い場合は `includeGeometry:false` で軽量化してから対象リージョンを絞り込み、次項の `get_face_region_detail` で必要なリージョンのみ精密取得する運用が安全です。

---

## 2) get_face_region_detail（リージョン詳細）
特定リージョンの詳細情報（重心・BBox・境界ループなど）を取得します。

- 主なパラメータ
  - `elementId` または `uniqueId`
  - `faceIndex`
  - `regionIndex`: `get_face_regions` で得た対象リージョンの `index`
  - `includeGeometry`, `includeMesh`: 必要に応じて境界／メッシュを含める

- 返却（概要）
  - `detail`:
    - `isPainted`, `material`, `area{...}`
    - `centroidMm`（mm重心）, `bboxMm{min,max}`（mm境界箱）
    - `plane`, `boundaryLoops`（ポリライン点群, mm）, `mesh`（任意）, `stats{totalPoints}`
    - 空間関連の付加情報（`spatial.primaryRoom` など）が付く場合あり

- 実行例（最小面積リージョン index=2 の詳細）
```bash
python Scripts/Reference/send_revit_command_durable.py --port 5210 --command get_face_region_detail \
  --params '{
    "elementId":61124693,
    "faceIndex":1,
    "regionIndex":2,
    "includeGeometry":true,
    "includeMesh":false
  }' \
  --output-file data/floor_61124693_region2_detail.json
```

- 取得例（抜粋）
  - `area.m2`: 9.0
  - `centroidMm`: (約) (3445.0, 5131.1266, 4100.0)
  - `bboxMm.min`: (1945.0, 3631.1266, 4100.0), `bboxMm.max`: (4945.0, 6631.1266, 4100.0)
  - `boundaryLoops` は矩形5点（開始=終了）

---

## 3) get_face_region_takeoff（リージョン数量・集計）
対象フェイスのリージョンごとの数量内訳と、マテリアル別集計などを取得します。

- 主なパラメータ
  - `elementId` または `uniqueId`
  - `faceIndex`
  - `regionIndex`（任意）：特定リージョンにフォーカスしたい場合に指定可

- 返却（概要）
  - `regions`[]: `regionIndex`, `material`, `area.m2`, `centroidMm`, `bboxMm`, `stableRep` など（`loops` は軽量応答では null の場合あり）
  - `summary.byMaterial`（`materialId` ごとの面積合計、件数など）, `summary.totalM2`

- 実行例（faceIndex=1 のリージョン内訳と集計）
```bash
python Scripts/Reference/send_revit_command_durable.py --port 5210 --command get_face_region_takeoff \
  --params '{
    "elementId":61124693,
    "faceIndex":1,
    "regionIndex":2
  }' \
  --output-file data/floor_61124693_region2_takeoff.json
```

- 取得例（抜粋）
  - `regions` に index=0/1/2 の3リージョンが並列で入り、各 `area.m2` が列挙
  - `summary.totalM2` は全リージョン合計（例: 804.8225）

---

## ベストプラクティス / トラブルシュート
- まず疎通確認: `ping_server` を実行して往復を確認
- faceIndex 特定: `get_paint_info` を `includeUnpainted:true` で確認
- 重い場合の軽量化:
  - `includeGeometry:false` / `includeMesh:false`
  - `tessellateChordMm` を大きく、`simplifyToleranceMm` を大きめ、`regionLimit` を小さく
- 高精度が必要な場合:
  - `tessellateChordMm` を小さく、`simplifyToleranceMm:0`
  - 必要リージョンのみ `get_face_region_detail` で精密取得
- コンソールの文字化け:
  - 出力は UTF-8 JSON。`--output-file` で保存したファイルをエディタで確認

---

## 参考: 本手順で保存したサンプル
- `data/floor_61124693_paint_info.json`
- `data/floor_61124693_face1_regions_with_geometry.json`
- `data/floor_61124693_region2_detail.json`
- `data/floor_61124693_region2_takeoff.json`

上記を雛形として、別要素・別フェイスでもパラメータを差し替えて実行できます。




