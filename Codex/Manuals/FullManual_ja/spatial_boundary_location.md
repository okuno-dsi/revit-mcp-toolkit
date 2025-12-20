# 空間境界（A/B）と `boundaryLocation`

## プロンプトで「境界線」と言われたら必ず確認
Revit では「境界線」という言葉が曖昧になりやすいです（例:「部屋の境界線」「外周線」「境界線をなぞって」「境界線を使って」など）。  
このような入力が来た場合は、**コマンド実行前に次のどちらを指すかユーザーに問い合わせてください**:

- **A) 計算境界（通常こちらが既定）**: `Autodesk.Revit.DB.Architecture.Room.GetBoundarySegments(...)`（計算結果。`boundaryLocation` の影響を受ける）
- **B) 線要素（境界線要素）**: Room/Space/Area の Separation/Boundary Lines（人が作図して初めて存在する CurveElement）

注意:
- 多くの部屋は **Room Separation Lines を持ちません**（壁が境界になるため）。その場合、B) は空になり得ますが、A) は取得できます。

## A) 計算境界（computed）
Revit の空間計算に基づく **計算結果の境界** を返す API です:
- `Room/Space/Area.GetBoundarySegments(SpatialElementBoundaryOptions)`
- `Room.IsPointInRoom(...)`, `Space.IsPointInSpace(...)`

このツールでは、上記 API を使うコマンドは通常 `boundaryLocation` を受け取ります。

### `boundaryLocation` の値
`boundaryLocation` は `SpatialElementBoundaryOptions.SpatialElementBoundaryLocation` に対応します:
- `Finish`（仕上げ面=内法）
- `Center`（壁芯）
- `CoreCenter`（コア芯/躯体芯）
- `CoreBoundary`（コア境界面）

用途の目安:
- `CoreCenter`: 計画・躯体芯ベースの扱い
- `Finish`: 設備・仕上げ計算など「内法」が意味を持つ扱い

## B) 境界線要素（CurveElement）
モデル内に存在する **線要素そのもの** を操作します（計算境界ではありません）:
- 部屋境界線（Room Separation Lines）
- スペース境界線（Space Separation Lines）
- エリア境界線（Area Boundary Lines）

境界線の作成/移動/トリム/削除コマンドはこの B) を対象にします。

## 例: 「部屋境界をなぞってエリア境界線を作る」
`copy_area_boundaries_from_rooms` を使い、意図に合わせて `boundaryLocation` を選びます:
- コア芯でエリア境界線: `boundaryLocation: "CoreCenter"`
- 内法でエリア境界線: `boundaryLocation: "Finish"`
