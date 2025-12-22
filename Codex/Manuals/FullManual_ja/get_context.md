# get_context

- カテゴリ: MetaOps
- 目的: `contextToken` / `contextRevision` を取得します（Step 7: 状態ドリフト検知）。

## 概要
- Canonical: `help.get_context`
- 旧エイリアス: `get_context`

以下のスナップショットを返します:
- アクティブドキュメント（title/path/guid）
- アクティブビュー（id/name/type）
- 現在選択（ID。必要に応じて省略/切り詰め）
- `contextRevision`（ドキュメント単位の単調増加カウンタ）
- `contextToken`（doc+view+revision+selection をハッシュ化）

また、多くのコマンドは統一レスポンスの `result.context.contextToken` にも `contextToken` を含めます。

## 使い方
- Method: `help.get_context`

### パラメータ
| 名前 | 型 | 必須 | 既定 |
|---|---|---|---|
| includeSelectionIds | boolean | no | true |
| maxSelectionIds | integer | no | 200 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "help.get_context",
  "params": { "includeSelectionIds": true, "maxSelectionIds": 200 }
}
```

### 戻り値例（形）
```jsonc
{
  "ok": true,
  "code": "OK",
  "data": {
    "tokenVersion": "ctx.v1",
    "revision": 123,
    "contextToken": "ctx-...",
    "docGuid": "...",
    "docTitle": "...",
    "docPath": "...",
    "activeViewId": 111,
    "activeViewName": "...",
    "activeViewType": "FloorPlan",
    "selectionCount": 2,
    "selectionIdsTruncated": false,
    "selectionIds": [1001, 1002]
  }
}
```

## `expectedContextToken` の使い方
多くのコマンドは `params.expectedContextToken` を任意で受け取れます。

- 現在の `contextToken` と一致しない場合:
  - `code: PRECONDITION_FAILED`
  - `msg: Context token mismatch...`
  - `nextActions` に `help.get_context` が入ります

注意:
- `help.get_context` / `get_context` は、誤って `expectedContextToken` を付けても必ず実行されるため、復旧（トークン再取得）が可能です。

