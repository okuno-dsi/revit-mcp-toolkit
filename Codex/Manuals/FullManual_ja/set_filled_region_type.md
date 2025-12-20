# set_filled_region_type

- カテゴリ: Annotations/FilledRegion
- 用途: 既存の FilledRegion 要素のタイプ（パターン・色）を一括で変更します。

## 概要
1 つ以上の `FilledRegion` インスタンスに対して、指定した `FilledRegionType` を割り当て直します。  
境界形状はそのまま、塗りパターンや色だけを切り替えたい場合に使います。

## 使い方
- メソッド: `set_filled_region_type`

### パラメータ
```jsonc
{
  "elementIds": [600001, 600002],
  "typeId": 12345
}
```

- `elementIds` (int[], 必須)
  - 対象とする FilledRegion 要素の ID 配列。
- `typeId` (int, 必須)
  - 変更先の `FilledRegionType` ID。`get_filled_region_types` で候補を取得できます。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": "region-type-swap",
  "method": "set_filled_region_type",
  "params": {
    "elementIds": [600001, 600002],
    "typeId": 12345
  }
}
```

## 戻り値
```jsonc
{
  "ok": true,
  "msg": "Updated 2 regions. 0 regions failed.",
  "stats": {
    "successCount": 2,
    "failureCount": 0
  },
  "results": [
    { "elementId": 600001, "ok": true },
    { "elementId": 600002, "ok": true }
  ]
}
```

## 補足

- 1 回のトランザクションで全ての ID を処理します。存在しない ID や FilledRegion 以外の要素は自動的にスキップされます。
- `typeId` がドキュメント内に存在しない場合は、全体が `ok: false` で返ります。
- 見た目（パターン・色）はタイプ側で管理されるため、「FilledRegionType を切り替える＝見た目を切り替える」と理解してください。

## 関連コマンド
- `get_filled_region_types`
- `get_filled_regions_in_view`
- `create_filled_region`

