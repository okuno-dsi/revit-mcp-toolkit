# delete_filled_region

- カテゴリ: Annotations/FilledRegion
- 用途: 1 つ以上の FilledRegion 要素を削除します。

## 概要
指定した `FilledRegion` インスタンスをドキュメントから削除します。  
存在しない ID や FilledRegion 以外の要素は自動的にスキップされ、実際に削除できた件数が集計されます。

## 使い方
- メソッド: `delete_filled_region`

### パラメータ
```jsonc
{
  "elementIds": [600001, 600002, 600003]
}
```

- `elementIds` (int[], 必須)
  - 削除候補の要素 ID 配列。FilledRegion 以外の要素 ID が混じっていても無視されます。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": "region-delete",
  "method": "delete_filled_region",
  "params": {
    "elementIds": [600001, 600002]
  }
}
```

## 戻り値
```jsonc
{
  "ok": true,
  "msg": "Deleted 2 filled regions.",
  "deletedCount": 2,
  "requestedCount": 2,
  "deletedElementIds": [600001, 600002]
}
```

## 補足

- 1 回のトランザクションで全ての ID を処理し、削除に成功した要素だけ `deletedElementIds` に含まれます。
- FilledRegion の削除に伴って、関連する注記グラフィックスが一緒に削除される場合があります（Revit の既定動作）。
- ビューごとに整理したい場合は、事前に `get_filled_regions_in_view` で対象を絞り込んでからこのコマンドを呼び出してください。

## 関連コマンド
- `get_filled_regions_in_view`
- `create_filled_region`
- `replace_filled_region_boundary`

