# get_face_region_takeoff

- カテゴリ: ElementOps
- 目的: このコマンドは『get_face_region_takeoff』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_face_region_takeoff

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| elementId | int | いいえ/状況による | 0 |
| faceIndex | unknown | いいえ/状況による |  |
| includeLoops | bool | いいえ/状況による | false |
| includeRoom | bool | いいえ/状況による | true |
| includeSpace | bool | いいえ/状況による | false |
| maxPointsPerLoop | int | いいえ/状況による | 300 |
| probeCount | int | いいえ/状況による | 5 |
| probeOffsetMm | number | いいえ/状況による | 5.0 |
| probeStrategy | string | いいえ/状況による | cross |
| regionLimit | int | いいえ/状況による | 50 |
| regionOffset | int | いいえ/状況による | 0 |
| returnProbeHits | bool | いいえ/状況による | false |
| simplifyToleranceMm | number | いいえ/状況による | 20.0 |
| tessellateChordMm | number | いいえ/状況による | 100.0 |
| uniqueId | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_face_region_takeoff",
  "params": {
    "elementId": 0,
    "faceIndex": "...",
    "includeLoops": false,
    "includeRoom": false,
    "includeSpace": false,
    "maxPointsPerLoop": 0,
    "probeCount": 0,
    "probeOffsetMm": 0.0,
    "probeStrategy": "...",
    "regionLimit": 0,
    "regionOffset": 0,
    "returnProbeHits": false,
    "simplifyToleranceMm": 0.0,
    "tessellateChordMm": 0.0,
    "uniqueId": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- get_material_parameters
- list_material_parameters
- update_material_parameter
- duplicate_material
- rename_material
- delete_material
- create_material
- 