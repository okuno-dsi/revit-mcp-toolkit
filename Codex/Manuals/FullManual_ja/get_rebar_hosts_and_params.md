# get_rebar_hosts_and_params

- カテゴリ: Rebar
- 目的: 複数の鉄筋要素について、ホスト情報と指定パラメータを取得する。

## 概要
Rebar / RebarInSystem / AreaReinforcement / PathReinforcement を対象に、**ホストID** と **タイプ情報**、指定した **パラメータ値** を返します。

## 使い方
- Method: get_rebar_hosts_and_params

### パラメータ例
```jsonc
{
  "elementIds": [5785112, 5785113, 5785114],
  "includeHost": true,
  "includeParameters": ["モデル鉄筋径", "鉄筋番号", "ホスト カテゴリ"],
  "includeTypeInfo": true,
  "includeErrors": true
}
```

- `elementIds` (int[], 必須)
- `includeHost` (bool, 既定 true)
- `includeParameters` (string[], 既定: ["モデル鉄筋径","鉄筋番号","ホスト カテゴリ"])
- `includeTypeInfo` (bool, 既定 true)
- `includeErrors` (bool, 既定 true)

### 出力例
```jsonc
{
  "ok": true,
  "count": 2,
  "items": [
    {
      "elementId": 5785112,
      "uniqueId": "...",
      "typeId": 4857541,
      "typeName": "D25",
      "familyName": "鉄筋棒",
      "hostId": 123456,
      "hostCategory": "構造柱",
      "parameters": {
        "モデル鉄筋径": "25 mm",
        "鉄筋番号": "1",
        "ホスト カテゴリ": "構造柱"
      }
    }
  ],
  "errors": [
    { "elementId": 5785114, "code": "NOT_FOUND", "msg": "Element not found." }
  ]
}
```

## 注意
- 要素側にパラメータが無い場合、タイプパラメータも検索します。
- `includeErrors=true` で取得失敗の詳細を返します。

- 英語版: `../FullManual/get_rebar_hosts_and_params.md`
