# set_wall_top_to_overhead

- カテゴリ: Walls
- 目的: 壁の上端を、直上の要素（床/屋根/天井/梁など）に合わせて更新します（レイキャスト + 可能ならアタッチ）。

## 概要
壁の基準線上の複数点から上方向へレイを飛ばし、最も支配的（ヒット回数が多い）な直上要素を推定します。

- `mode:"auto"`: 直上要素が床/屋根であれば `WallUtils.AttachWallTop` を試行（勾配屋根に強い）。それ以外はヒットしたZから `WALL_TOP_OFFSET`（または未接続高さ）を設定します。
- `mode:"attach"`: 床/屋根へのアタッチのみ。失敗時はレイキャストにフォールバックしません。
- `mode:"raycast"`: アタッチせず、Zから上端高さを設定します（単一高さ）。

注意:
- 3Dビューが必要です（テンプレート/パースは不可）。`{3D}` があればそれを使用します。存在しない場合、`apply:true` のとき一時3Dビューを作成することがあります。
- 勾配屋根/勾配床で「BBoxのminZ」などを使うと壁が短くなることがあるため、基本は `auto`（アタッチ優先）推奨です。
- 壁がすでにターゲットより高い可能性がある場合（例: 天井に合わせて壁を“短くする”）は、`startFrom:"wallBase"`（または `startFrom:"base"`）を指定して、レイの開始点をターゲットより下にしてください。

## 使い方
- メソッド: set_wall_top_to_overhead

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| wallId | int | いいえ/状況による | （選択壁） |
| wallIds | int[] | いいえ/状況による | （選択壁） |
| mode | string | いいえ | auto |
| categoryIds | int[] | いいえ | 床/屋根/天井/構造フレーム |
| view3dId | int | いいえ | 自動 |
| includeLinked | bool | いいえ | false |
| sampleFractions | number[] | いいえ | [0.1,0.3,0.5,0.7,0.9] |
| startFrom | string | いいえ | wallTop |
| startBelowTopMm | number | いいえ | 10 |
| maxDistanceMm | number | いいえ | 50000 |
| zAggregate | string | いいえ | max |
| apply | bool | いいえ | true |
| dryRun | bool | いいえ | false |

### リクエスト例（選択中の壁を対象）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_wall_top_to_overhead",
  "params": {}
}
```

### リクエスト例（床/屋根のみ、ドライラン）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_wall_top_to_overhead",
  "params": {
    "mode": "raycast",
    "categoryIds": [-2000032, -2000035],
    "dryRun": true
  }
}
```

## 関連コマンド
- update_wall_parameter
- get_wall_baseline
- get_bounding_box
- create_section
