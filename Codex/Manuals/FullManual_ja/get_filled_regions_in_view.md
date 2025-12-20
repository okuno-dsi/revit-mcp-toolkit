# get_filled_regions_in_view

- カテゴリ: Annotations/FilledRegion
- 用途: 指定したビュー（またはアクティブビュー）内の FilledRegion 要素一覧を取得します。

## 概要
対象ビュー内に配置された `FilledRegion` をすべて列挙し、要素 ID、タイプ ID、ビュー ID、マスキングフラグなどの基本情報を返します。  
どのビューにどの塗りつぶし領域があるかを把握したり、一括変更前の確認に使います。

## 使い方
- メソッド: `get_filled_regions_in_view`

### パラメータ
```jsonc
{
  "viewId": 11120738   // 任意。省略時はアクティブビュー
}
```

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_filled_regions_in_view",
  "params": {
    "viewId": 11120738
  }
}
```

## 戻り値
```jsonc
{
  "ok": true,
  "viewId": 11120738,
  "totalCount": 3,
  "items": [
    {
      "elementId": 600001,
      "uniqueId": "....",
      "typeId": 12345,
      "viewId": 11120738,
      "isMasking": false
    }
  ]
}
```

## 補足

- テンプレートビューは対象外です（FilledRegion の作成・編集ができないため）。
- 取得した `elementId` は `set_filled_region_type` や `delete_filled_region` などの後続コマンドにそのまま渡せます。

## 関連コマンド
- `get_filled_region_types`
- `create_filled_region`
- `delete_filled_region`

