# get_rooms

- カテゴリ: Room
- 目的: 部屋一覧を取得します。

## 概要
`get_rooms` は、Revit 内の部屋をページング付きで取得するコマンドです。

主な用途:
- レベル別の部屋一覧取得
- 部屋名を含む検索
- 面積・番号・レベル名・中心座標の取得
- `docGuid` などを指定した非アクティブ文書の読取

## 使い方
- メソッド: `get_rooms`

### パラメータ
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---|---|---|
| `skip` | int | いいえ | `0` | 取得開始位置 |
| `count` | int | いいえ | 全件 | 取得件数 |
| `level` | string | いいえ |  | レベル名で絞り込み |
| `nameContains` | string | いいえ |  | 部屋名の部分一致 |
| `compat` | bool | いいえ | `false` | `roomsById` も返す互換形式 |
| `docGuid` | string | いいえ |  | 対象文書の `docGuid` / `docKey` |
| `docTitle` | string | いいえ |  | 対象文書タイトル |
| `docPath` | string | いいえ |  | 対象文書フルパス |

補足:
- `docGuid` / `docTitle` / `docPath` を省略した場合は、アクティブ文書を対象にします。
- `docGuid` は `get_open_documents` や `get_context` で確認できます。
- `meta.extensions.docGuid` / `docTitle` / `docPath` でも同様に指定できます。
- `count=0` を明示すると、件数確認用の軽量レスポンスを返します。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_rooms",
  "params": {
    "docGuid": "ctx-doc-guid-or-dockey",
    "skip": 0,
    "count": 50,
    "level": "3FL",
    "nameContains": "事務室",
    "compat": false
  }
}
```

### レスポンス例
```json
{
  "ok": true,
  "totalCount": 2,
  "rooms": [
    {
      "elementId": 123456,
      "uniqueId": "....",
      "name": "事務室3-1",
      "number": "83",
      "level": "3FL",
      "area": 309.71
    }
  ]
}
```

## 関連コマンド
- `get_context`
- `get_open_documents`
- `get_room_params`
- `set_room_param`
- `summarize_rooms_by_level`
- `get_spatial_params_bulk`
