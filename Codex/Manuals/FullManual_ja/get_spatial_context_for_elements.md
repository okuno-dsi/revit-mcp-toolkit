# get_spatial_context_for_elements

- カテゴリ: Spatial
- 目的: 複数の要素について、端点・中間点を含むサンプリング点ごとに Room / Space / Area に含まれるかどうかを一括取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、`elementIds` で指定した複数要素について、

- 代表点（Location / BoundingBox 中心）
- 曲線要素の場合は端点・中間点などのサンプリング点

で空間要素 (Room / Space / Area) を調べ、ヒットしたものだけを結果に含めます。

`get_spatial_context_for_element` の「多要素版」であり、端点・中間点を使った空間包含チェックの自動化に向いています。

## 使い方
- メソッド: get_spatial_context_for_elements

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| elementIds | int[] | はい | - |
| phaseName | string | いいえ |  |
| mode | string | いいえ | `"3d"` |
| include | string[] | いいえ | (省略時は全て) |
| curveSamples | number[] | いいえ | `[0.0, 0.5, 1.0]` |
| maxElements | int | いいえ | `int.MaxValue` |

- `elementIds`  
  - 対象とする要素の `ElementId.IntegerValue` の配列です。  
  - 「座標が取れるカテゴリすべて」を対象にしたい場合は、先に別コマンド（例: `get_elements_in_view` やカテゴリ別取得コマンド）で ID を集めてから、このコマンドに渡します。
- `phaseName` / `mode` / `include`  
  - 単一版 `get_spatial_context_for_element` と同じ意味です。
  - `include` を省略すると Room / Space / Zone / Area / AreaScheme をすべて対象にします。
- `curveSamples`  
  - `LocationCurve` を持つ要素に対して、曲線パラメータ `t (0.0〜1.0)` でサンプリングする比率のリストです。
  - 既定は `[0.0, 0.5, 1.0]`（始点・中点・終点）。
- `maxElements`  
  - 一度に処理する最大要素数。大量の ID を渡す場合の安全弁として使用します。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_spatial_context_for_elements",
  "params": {
    "elementIds": [60500001, 60500002, 60500003],
    "phaseName": "新築",
    "mode": "3d",
    "include": ["room", "space", "area"],
    "curveSamples": [0.0, 0.25, 0.5, 0.75, 1.0]
  }
}
```

## レスポンス（成功時）

```jsonc
{
  "ok": true,
  "totalCount": 2,
  "elements": [
    {
      "elementId": 60500001,
      "category": "壁",
      "samples": [
        {
          "kind": "reference",
          "point": { "x": 10000.0, "y": 20000.0, "z": 0.0 },
          "room": { "id": 60535031, "name": "倉庫A 32", "number": "32", "phase": "", "levelName": "1FL" },
          "spaces": [],
          "areas": []
        },
        {
          "kind": "curveStart",
          "point": { "x": 9500.0, "y": 20000.0, "z": 0.0 },
          "room": null,
          "spaces": [
            { "id": 60576295, "name": "スペース 7", "number": "7", "levelName": "1FL", "zone": null }
          ],
          "areas": []
        }
      ]
    },
    {
      "elementId": 60500002,
      "category": "窓",
      "samples": [
        {
          "kind": "location",
          "point": { "x": 12000.0, "y": 21000.0, "z": 900.0 },
          "room": { "id": 60535025, "name": "執務室A 30", "number": "30", "phase": "", "levelName": "1FL" },
          "spaces": [],
          "areas": [
            {
              "id": 60576279,
              "name": "面積 7",
              "number": "7",
              "areaScheme": { "id": 9490, "name": "07 建面" }
            }
          ]
        }
      ]
    }
  ],
  "messages": [
    "Room was resolved using the final project phase.",
    "Found 1 Space(s) at the reference point.",
    "同レベルの Area による包含は見つかりませんでした（BoundingBox ベースの近似判定）。"
  ]
}
```

### 挙動のポイント
- 各要素ごとに:
  - `reference`（代表点）、`location`（LocationPoint）、`curveStart` / `curveMid` / `curveEnd` / `curveParam`（曲線サンプル点）について、Room / Space / Area を判定します。
  - どのサンプル点でも Room / Space / Area にヒットしなかった要素は `elements` から省かれます。
- `messages` には、各要素で生じた Room/Space/Area 解決メッセージが集約されます。

## 関連コマンド
- get_spatial_context_for_element  
- classify_points_in_room  
- get_spaces  
- get_areas  
