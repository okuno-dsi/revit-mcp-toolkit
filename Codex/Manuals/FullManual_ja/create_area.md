# create_area

- カテゴリ: Area
- 目的: （エリアプランで）指定したXY点に Area を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

重要
- Area は **AreaScheme** に属します。プロジェクトに複数の AreaScheme がある場合、`levelId` だけだと同一レベルの別スキームの AreaPlan が選ばれ、意図しないスキームに Area が作られることがあります。
- 目的の AreaScheme を確実に狙うには、`viewId`（推奨）または `viewUniqueId` で **Area Plan ビュー**を指定してください。

## 使い方
- メソッド: create_area

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| viewId | int | いいえ* | active/level lookup |
| viewUniqueId | string | いいえ* | active/level lookup |
| batchSize | int | いいえ/状況による | 50 |
| levelId | int | いいえ/状況による |  |
| maxMillisPerTx | int | いいえ/状況による | 100 |
| refreshView | bool | いいえ/状況による | false |
| startIndex | int | いいえ/状況による | 0 |

* `viewId/viewUniqueId`（推奨）または `levelId` のどちらかを指定してください。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_area",
  "params": {
    "viewId": 11120260,
    "x": 21576.985,
    "y": 5852.247,
    "refreshView": true
  }
}
```

## 関連コマンド
## 関連コマンド
- get_areas
- get_area_params
- update_area
- move_area
- delete_area
- get_area_boundary_walls
- get_area_centroid
- 
