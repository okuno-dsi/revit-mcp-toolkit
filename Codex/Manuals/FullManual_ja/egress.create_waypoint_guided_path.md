# egress.create_waypoint_guided_path

- カテゴリ: Route
- 目的: Revit の PathOfTravel を用いて、ウェイポイント誘導の移動経路（避難経路）を作成します。

## 概要
平面ビューで `PathOfTravel` 要素を作成し、必要に応じて **2〜5個程度のウェイポイント** を設定して、最短経路ではないルートへ誘導します。

対応モード:
- `roomMostRemoteToDoor`: 指定した部屋の「指定ドアから最も遠い点」をグリッドサンプリングで算出し、そこからターゲットドアまでルート作成
- `pointToDoor`: 指定した開始点からターゲットドアまでルート作成

## 使い方
- メソッド: `egress.create_waypoint_guided_path`
- トランザクション: Write
- エイリアス: `revit.egress.createWaypointGuided`, `egress.create_waypoint_guided`

## パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---:|---|
| viewId | int | いいえ | ActiveView |
| mode | string | はい | `"roomMostRemoteToDoor"` |
| targetDoorId | int | はい |  |
| waypoints | point[] | いいえ | [] |
| clearanceMm | number | いいえ | 450 |
| doorApproachOffsetMm | number | いいえ | `clamp(clearanceMm, 200..600)` |
| createLabel | bool | いいえ | false |
| labelFormat | string | いいえ | `"{len_m:0.00} m"` |
| labelOffsetMm | number | いいえ | 300 |
| createTag | bool | いいえ | false |
| tagTypeId | int | いいえ |  |
| tagTypeFromSelection | bool | いいえ | true |
| tagAddLeader | bool | いいえ | true |
| tagOrientation | string | いいえ | `"Horizontal"` |
| tagOffsetMm | number | いいえ | 300 |
| forcePassThroughRoomDoor | bool | いいえ | true |
| gridSpacingMm | number | いいえ | 600 |
| maxSamples | int | いいえ | 2000 |
| maxSolverCandidates | int | いいえ | 200 |

モード別パラメータ:

### mode = `roomMostRemoteToDoor`
| 名前 | 型 | 必須 |
|---|---|---:|
| roomId | int | はい |
| roomDoorId | int | はい |

### mode = `pointToDoor`
| 名前 | 型 | 必須 |
|---|---|---:|
| start | point | はい |

点の形式（mm）:
```json
{ "x": 0.0, "y": 0.0, "z": 0.0 }
```

補足:
- `ViewPlan`（床プラン/天井プラン）でのみ動作します。
- Z はビューのレベル標高にスナップされます。
- `PathOfTravel` は **ビューで見えているジオメトリ** を障害物として扱うため、クロップ/表示設定で結果が変わります。
- `createTag=true` の場合、`tagTypeId` が無いときは選択中タグから解決します。見つからなければ `ok:false` で終了します。

## リクエスト例（roomMostRemoteToDoor）
```json
{
  "jsonrpc": "2.0",
  "id": "egress-1",
  "method": "egress.create_waypoint_guided_path",
  "params": {
    "viewId": 123456,
    "mode": "roomMostRemoteToDoor",
    "roomId": 456789,
    "roomDoorId": 111222,
    "targetDoorId": 333444,
    "clearanceMm": 450,
    "waypoints": [
      { "x": 2000, "y": 3000, "z": 0 },
      { "x": 6000, "y": 3000, "z": 0 }
    ],
    "createTag": true,
    "tagTypeFromSelection": true,
    "createLabel": true,
    "labelFormat": "{len_m:0.00} m",
    "labelOffsetMm": 300
  }
}
```

## 戻り値
- `data.pathId`: 作成した `PathOfTravel` の要素ID
- `data.statusCreate`: `Create(...)` の `PathOfTravelCalculationStatus`
- `data.status`: ウェイポイント適用後 `Update()` の `PathOfTravelCalculationStatus`
- `data.lengthM`: ルート長さ（m）
- `data.startPoint`, `data.endPoint`: 使用した点（mm）
- `data.waypointsApplied`: ウェイポイント数
- `data.labelId`: 付与したテキストの要素ID（作成しない場合は 0）
- `data.debug.roomMostRemote`: `roomMostRemoteToDoor` のときにサンプリング情報を返します
- `warnings[]`: 非致命の注意（クロップ影響、ラベル作成失敗など）
