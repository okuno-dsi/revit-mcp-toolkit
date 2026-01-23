# route.find_shortest_paths

- カテゴリ: Route
- 目的: 平面ビュー上で最短経路のポリラインを求めます（解析のみ。要素は作成しません）。

## 概要
`Autodesk.Revit.DB.Analysis.PathOfTravel.FindShortestPaths(...)` のラッパです。

- **平面ビュー**（`ViewPlan`: 床プラン / 天井プラン）でのみ動作します。
- 障害物は **ビューで見えているジオメトリ** に依存します（クロップ/表示設定で結果が変わります）。
- 結果はポリライン（点列）として返ります（座標: mm、長さ: m）。

## 使い方
- メソッド: `route.find_shortest_paths`
- トランザクション: Read

### パラメータ
次のどちらかの形式で呼び出せます。

1) start/end（単一）
| 名前 | 型 | 必須 |
|---|---|---:|
| viewId | int | いいえ |
| start | point | はい |
| end | point | はい |

2) starts/ends（複数・ベストエフォート）
| 名前 | 型 | 必須 |
|---|---|---:|
| viewId | int | いいえ |
| starts | point[] | はい |
| ends | point[] | はい |

点の形式（mm）:
```json
{ "x": 0.0, "y": 0.0, "z": 0.0 }
```

補足:
- Z はビューのレベル標高にスナップされます。
- 本コマンドは `PathOfTravel` 要素を作成しません。

### リクエスト例（単一）
```json
{
  "jsonrpc": "2.0",
  "id": "route-1",
  "method": "route.find_shortest_paths",
  "params": {
    "viewId": 123456,
    "start": { "x": 1000, "y": 1000, "z": 0 },
    "end": { "x": 9000, "y": 1000, "z": 0 }
  }
}
```

## 戻り値
- `ok`: boolean
- `data.items[]`: `{ index, lengthM, points[] }`
- `data.bestIndex`, `data.bestLengthM`

ルートが見つからない場合は `ok:false`、`code=NO_PATH`、`nextActions[]` を返します。

