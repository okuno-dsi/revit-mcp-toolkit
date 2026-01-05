# rebar_regenerate_delete_recreate

- カテゴリ: Rebar
- 目的: 選択したホスト（柱/梁）配下の「ツール生成鉄筋」を削除し、最新パラメータから再作成し、さらに署名（signature）を Revit 内の ledger に保存します。

## 概要
- まず plan を作成します（`rebar_plan_auto` と同等の入力で計画）。
- ホストごとにトランザクションを分離して、以下を実行します:
  1) 削除（安全のため「ホスト内」かつ「タグ一致」のみ）
     - `RebarHostData.GetRebarsInHost()` の範囲
     - Rebar の `Comments` が `tag` を含む
  2) plan の actions に従って鉄筋を再作成
  3) レシピ署名（SHA-256）を ledger に保存

## 使い方
- Method: `rebar_regenerate_delete_recreate`

### パラメータ
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---:|---:|---|
| hostElementIds | int[] | no |  | 未指定なら選択を使います（`useSelectionIfEmpty=true`）。 |
| useSelectionIfEmpty | bool | no | true | `hostElementIds` が空なら選択を使う。 |
| profile | string | no |  | `RebarMapping.json` のプロファイル名（任意）。 |
| options | object | no |  | `rebar_plan_auto.options` と同じ（計画に影響）。 |
| tag | string | no | `RevitMcp:AutoRebar` | 削除対象のフィルタタグ（Rebar `Comments` に含まれるもの）。未指定時は `options.tagComments` を優先。 |
| deleteMode | string | no | `tagged_only` | 安全のため現状はこれのみ対応。 |
| storeRecipeSnapshot | bool | no | false | true の場合、recipe 全体を ledger に保存（デバッグ用。RVTが肥大化する可能性）。 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "rebar_regenerate_delete_recreate",
  "params": {
    "useSelectionIfEmpty": true,
    "profile": "default",
    "tag": "RevitMcp:AutoRebar",
    "deleteMode": "tagged_only",
    "options": {
      "mainBarTypeName": "D22",
      "tieBarTypeName": "D10",
      "tagComments": "RevitMcp:AutoRebar"
    }
  }
}
```

## 注意
- **削除を含む write コマンド**です（高リスク）: ただし削除対象は「ホスト内」かつ「タグ一致」に限定します。
- ledger DataStorage は「無ければ作る」だけで、`rebar_sync_status` だけでは作成されません。
- ledger の書き込みは delete/recreate と同一トランザクション内で行い、書き込みに失敗した場合はそのホスト分をロールバックします。

## 関連
- `rebar_sync_status`
- `rebar_plan_auto`
- `rebar_apply_plan`

