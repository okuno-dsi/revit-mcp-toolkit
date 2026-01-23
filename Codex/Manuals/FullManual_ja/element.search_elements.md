# element.search_elements

- カテゴリ: ElementOps/Query
- 目的: 要素IDが分からないときに、キーワード検索で要素候補（ElementIds）を発見します。

## 概要
要素ID（ElementId）が不明な状態からスタートするための **発見用** コマンドです。

- 要素の **名前 / カテゴリ / UniqueId / タイプ名** を対象にキーワード一致で検索します。
- `categories` / `viewId` / `levelId` による絞り込みが可能です。
- 戻り値の `elements[]`（要約）からIDを取り出して、次の処理（移動/更新/着色など）へ渡します。

## 使い方
- メソッド: `element.search_elements`

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| keyword | string | はい |  |
| categories | string[] | いいえ | []（全カテゴリ） |
| viewId | int | いいえ | null |
| levelId | int | いいえ | null |
| includeTypes | bool | いいえ | false |
| caseSensitive | bool | いいえ | false |
| maxResults | int | いいえ | 50（上限: 500） |

補足:
- `categories[]` は、英語/日本語の代表名と `OST_*` 名（BuiltInCategory）を受け付けます。
  - 例: `"Walls"`, `"Doors"`, `"Rooms"` / `"壁"`, `"建具"`, `"部屋"` / `"OST_Walls"`

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": "find-doors",
  "method": "element.search_elements",
  "params": {
    "keyword": "Single",
    "categories": ["Doors"],
    "viewId": 12345,
    "maxResults": 50
  }
}
```

## 戻り値
- `ok`: boolean
- `elements[]`: `{ id, uniqueId, name, category, className, levelId, typeId }`
- `counts`: `{ candidates, returned }`
- `warnings[]`: カテゴリ名の解決に失敗したものがある場合に返ります

## 備考
- `categories` と `viewId` の両方を省略すると、モデル全体を走査するため時間がかかることがあります。可能なら絞り込みを併用してください。
- `categories` を指定したが全て解決できない場合は `ok:false`（`code=INVALID_CATEGORY`）で終了します。

