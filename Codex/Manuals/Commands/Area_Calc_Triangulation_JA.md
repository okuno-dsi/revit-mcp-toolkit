# Area Calc Triangulation (JA)

対象コマンド:
- `area.calc_edge_lengths` / `calc_area_edge_lengths`
- `area.calc_run_all` / `calc_area_run_all`

## 概要
- 面積算定は、境界ループを **三角形分割（ear clipping）** して求めます。
- `KSG_Formula` は原則 `A=ΣTri(n)/1e6` の形式になります。
- 三角形分割に失敗したループのみ、従来の shoelace にフォールバックします（警告を返却）。

## 長方形の扱い
- 線分4辺の長方形は、**頂点を追加せず** 既存4頂点の対角で2三角形に分割します。
- つまり「長方形部分をその頂点を結ぶ三角形に分割して求積」に対応しています。

## 仕様メモ
- ループ向き（外周/内周）を保持したまま符号付き面積を合算します。
- 境界が直線の場合は端点のみ使用し、不要な点増加を抑制します。
- 円弧など非直線境界はテッセレーション点を使用します。

## レスポンス確認ポイント
- `items[].triangleCount`: 面積計算に使った三角形数
- `items[].formula`: `ΣTri` またはフォールバック時の `shoelace`
- `items[].warnings`: フォールバックなどの警告
