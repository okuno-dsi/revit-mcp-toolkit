# reset_tag_colors

- カテゴリ: VisualizationOps
- 目的: アクティブビュー（または選択）内の注釈タグに対して設定された `OverrideGraphicSettings` をリセットし、初期表示に戻します。

## 概要
このコマンドは、部屋タグ・ドアタグなどの注釈タグに対して適用された要素ごとのグラフィック上書きをクリアします。タグ以外の要素には影響しないよう、対象カテゴリを絞り込んで処理します。

## 使い方
- メソッド: reset_tag_colors

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| targetCategories | string[] | いいえ | ["OST_RoomTags","OST_DoorTags","OST_WindowTags","OST_GenericAnnotation"] |

`targetCategories` を指定しない場合は、代表的なタグカテゴリが既定で使われます。値は `BuiltInCategory` 名（例: `"OST_RoomTags"`）で指定します。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "reset_tag_colors",
  "params": {
    "targetCategories": [
      "OST_RoomTags",
      "OST_DoorTags"
    ]
  }
}
```

スコープ:
- 何か要素が選択されている場合は、その選択のうち `targetCategories` に含まれるカテゴリの要素だけをリセットします。
- 選択がない場合は、アクティブビュー内で `targetCategories` に含まれるカテゴリの要素をすべてリセットします。

## 関連コマンド
- colorize_tags_by_param
- clear_visual_override

### Params スキーマ
```json
{
  "type": "object",
  "properties": {
    "targetCategories": {
      "type": "array"
    }
  },
  "additionalProperties": true
}
```

### Result スキーマ
```json
{
  "type": "object",
  "properties": {},
  "additionalProperties": true
}
```

