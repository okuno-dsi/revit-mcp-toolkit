# spatial.suggest_params

- カテゴリ: Spatial
- 目的: Room / Space / Area のパラメータ名を候補提示（曖昧語検索）します。

## 概要
ユーザーが曖昧なパラメータ名（例: 「天井高」「面積」など）を指定した場合に、候補を提示するヘルパーです。

- モデルは変更しません。
- 既定では kind ごとに最大 30 要素をサンプルとして解析します。
- 候補確定後は `get_spatial_params_bulk` などで実値を取得してください。

## 使い方
- メソッド: spatial.suggest_params

### パラメータ
| 名前 | 型 | 必須 | 説明 |
|---|---|---|---|
| kind | string | いいえ | `room` / `space` / `area` |
| kinds | string[] | いいえ | 複数指定 |
| hint | string | いいえ | 1件のヒント語 |
| hints | string[] | いいえ | 複数ヒント |
| matchMode | string | いいえ | `exact` / `contains` / `fuzzy`（既定: `fuzzy`） |
| maxMatchesPerHint | int | いいえ | ヒントごとの最大候補数（既定: 5） |
| maxCandidates | int | いいえ | `includeAllCandidates=true` 時の最大候補数 |
| sampleLimitPerKind | int | いいえ | kind ごとのサンプル数 |
| includeAllCandidates | bool | いいえ | 候補一覧を返す（ヒント無し時は既定 true） |
| elementIds | int[] | いいえ | サンプル対象の要素ID |
| all | bool | いいえ | 全要素からサンプル（`elementIds` 空なら既定 true） |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "spatial.suggest_params",
  "params": {
    "kinds": ["room", "space"],
    "hints": ["天井高", "面積"],
    "matchMode": "fuzzy",
    "maxMatchesPerHint": 5,
    "sampleLimitPerKind": 30
  }
}
```

## レスポンス
```jsonc
{
  "ok": true,
  "kinds": ["room", "space"],
  "hints": ["天井高", "面積"],
  "matchMode": "fuzzy",
  "maxMatchesPerHint": 5,
  "sampleLimitPerKind": 30,
  "items": [
    {
      "kind": "room",
      "elementSampleCount": 30,
      "totalCandidates": 128,
      "byHint": [
        {
          "hint": "天井高",
          "matches": [
            {
              "name": "天井高",
              "id": 123456,
              "storageType": "Double",
              "dataType": "autodesk.spec.aec:length-2.0.0",
              "count": 30,
              "score": 100
            }
          ]
        }
      ]
    }
  ]
}
```

## 注意
- 候補は **サンプル要素** に基づくため、サンプルに存在しない共有パラメータは出ない場合があります。
- 確定後は `get_spatial_params_bulk` などで取得してください。
