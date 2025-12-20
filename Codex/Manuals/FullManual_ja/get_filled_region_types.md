# get_filled_region_types

- カテゴリ: Annotations/FilledRegion
- 用途: 現在のドキュメント内に定義されている FilledRegionType 一覧を取得します。

## 概要
`FilledRegionType`（塗りつぶし領域タイプ）の一覧を取得し、塗りパターン ID/名前と前景色・背景色を返します。  
どのタイプを使って FilledRegion を作成／変更するかを決めるための事前調査に使います。

## 使い方
- メソッド: `get_filled_region_types`
- パラメータ: なし

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_filled_region_types",
  "params": {}
}
```

## 戻り値
```jsonc
{
  "ok": true,
  "totalCount": 5,
  "items": [
    {
      "typeId": 12345,
      "uniqueId": "....",
      "name": "Solid Black",
      "isMasking": false,
      "foregroundPatternId": 2001,
      "foregroundPatternName": "Solid fill",
      "foregroundColor": { "r": 0, "g": 0, "b": 0 },
      "backgroundPatternId": null,
      "backgroundPatternName": null,
      "backgroundColor": null
    }
  ]
}
```

## 補足

- `isMasking` が true のタイプは、下の要素をマスクするタイプです（図面の強調などに便利です）。
- 実際の色やパターンの編集は Revit 側（タイププロパティ）で行います。

## 関連コマンド
- `get_filled_regions_in_view`
- `create_filled_region`
- `set_filled_region_type`

