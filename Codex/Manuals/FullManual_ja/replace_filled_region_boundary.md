# replace_filled_region_boundary

- カテゴリ: Annotations/FilledRegion
- 用途: 既存 FilledRegion の境界（形状）を、新しいポリゴンループで置き換えます。

## 概要
指定した `FilledRegion` の境界ループを、mm 単位の新しいポリゴンループで差し替えます。  
内部的には同じビュー・同じタイプで新しい FilledRegion を作成し、元の要素を削除して入れ替えます。

## 使い方
- メソッド: `replace_filled_region_boundary`

### パラメータ
```jsonc
{
  "elementId": 600001,
  "loops": [
    [
      { "x": 0, "y": 0, "z": 0 },
      { "x": 1200, "y": 0, "z": 0 },
      { "x": 1200, "y": 600, "z": 0 },
      { "x": 0, "y": 600, "z": 0 }
    ]
  ]
}
```

- `elementId` (int, 必須)
  - 形状を置き換える対象の FilledRegion の要素 ID。
- `loops` (配列, 必須)
  - 新しいポリゴンループ（`create_filled_region` と同形式）。mm 単位、3 点以上の頂点が必要です。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": "region-replace-boundary",
  "method": "replace_filled_region_boundary",
  "params": {
    "elementId": 600001,
    "loops": [
      [
        { "x": 0, "y": 0, "z": 0 },
        { "x": 1200, "y": 0, "z": 0 },
        { "x": 1200, "y": 600, "z": 0 },
        { "x": 0, "y": 600, "z": 0 }
      ]
    ]
  }
}
```

## 戻り値
```jsonc
{
  "ok": true,
  "oldElementId": 600001,
  "newElementId": 600010,
  "newUniqueId": "....",
  "viewId": 11120738
}
```

## 補足

- 新しい要素は元の FilledRegion と同じ `FilledRegionType` とビューを使用します。
- 新規作成＋元要素削除という挙動のため、`elementId` と `uniqueId` は必ず変わります。エージェント側で ID を保持している場合は、戻り値の `newElementId` を採用してください。
- 3 点未満、または有効なループが 1 つもない場合は `ok: false` で失敗します。

## 関連コマンド
- `create_filled_region`
- `delete_filled_region`
- `get_filled_regions_in_view`

