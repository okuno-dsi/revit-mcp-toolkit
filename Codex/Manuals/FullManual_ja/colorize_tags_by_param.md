# colorize_tags_by_param

- カテゴリ: VisualizationOps  
- 目的: アクティブビュー（または選択要素）のタグを、パラメータ値に応じて色分けします。

## 概要
このコマンドは、部屋タグやドアタグなどの注釈タグ要素に対して `OverrideGraphicSettings` を設定し、
指定した文字列パラメータの値と色マッピングに基づいて線色を変更します。  
必要に応じて、タグ自身ではなくホスト要素（Room など）のパラメータを参照することもできます。

## 使い方
- メソッド: `colorize_tags_by_param`

### パラメータ
すべて任意です。指定しない場合は既定値が使われます。

| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| `config.parameterName` | string | いいえ | `"Comments"` |
| `config.targetCategories` | string[] | いいえ | `["OST_RoomTags","OST_DoorTags","OST_WindowTags","OST_GenericAnnotation"]` |
| `config.mappings` | object | いいえ | `{}` |
| `config.defaultColor` | array / object | いいえ | `null` |
| `config.readFromHost` | bool | いいえ | `false` |

- `config.mappings` は「部分文字列 → 色 (RGB)」の対応表です（大文字小文字は無視）。  
  例: `"タイルカーペット": [255,0,0]`, `"フローリング": [0,128,0]`
- `config.defaultColor` は `[r,g,b]` あるいは `{ "r":0,"g":0,"b":255 }` の形式で、
  いずれのキーにもマッチしない場合やパラメータが空の場合に使われる色です。
- `config.readFromHost` を `true` にすると、まずホスト要素（Room / Door など）側の
  パラメータ値を読み、それが無い場合のみタグ自身のパラメータを参照します。

`config` オブジェクトは `params.config` の下に入れても、`parameterName` などを `params` の直下にフラットに指定しても構いません。

### 例: コメントでタグを色分け
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "colorize_tags_by_param",
  "params": {
    "config": {
      "parameterName": "Comments",
      "targetCategories": ["OST_RoomTags"],
      "mappings": {
        "FIRE": [255, 0, 0],
        "ACCESSIBLE": [0, 128, 0]
      },
      "defaultColor": [0, 0, 255],
      "readFromHost": false
    }
  }
}
```

### スコープ
- 要素が選択されている場合: 対象カテゴリかどうかに関わらず、**選択されている要素のみ** を処理します。  
- 何も選択されていない場合: アクティブビュー内の要素のうち、`targetCategories` に含まれるカテゴリのタグをすべて処理します。
- アクティブビューに View Template が適用されている場合で、事前にテンプレートを外していないときは、
  色変更は行われません。このとき結果には  
  `templateApplied: true` / `templateViewId` / `skippedDueToTemplate: true` / `errorCode: "VIEW_TEMPLATE_LOCK"` / `message`  
  などのプロパティが含まれ、テンプレートを外すように促す情報として利用できます。

## 関連コマンド
- `reset_tag_colors`
- `set_visual_override`
- `clear_visual_override`

### Params スキーマ
```json
{
  "type": "object",
  "properties": {
    "config": {
      "type": "object"
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

