# rebar_sync_status

- カテゴリ: Rebar
- 目的: 選択した配筋ホスト（柱/梁）が、直近の delete&recreate 実行時点と一致しているか（in sync）を、レシピ署名（signature）で判定します。

## 概要
- `rebar_plan_auto` と同じ計画ロジックで **現在のレシピ署名** を計算します（モデルは変更しません）。
- Revit 内に保存される **Rebar Recipe Ledger**（DataStorage + ExtensibleStorage）を読み取り、
  - `currentSignature`（今回計算）
  - `lastSignature`（直近の `rebar_regenerate_delete_recreate` が保存）
  を比較して `isInSync` を返します。
- まだ ledger が無い場合は `hasLedger:false` を返し、`lastSignature` は空になります。

## 使い方
- Method: `rebar_sync_status`

### パラメータ
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---:|---:|---|
| hostElementIds | int[] | no |  | 未指定なら選択を使います（`useSelectionIfEmpty=true`）。 |
| useSelectionIfEmpty | bool | no | true | `hostElementIds` が空なら選択を使う。 |
| profile | string | no |  | `RebarMapping.json` のプロファイル名（任意）。署名に影響します。 |
| options | object | no |  | `rebar_plan_auto.options` と同じ（署名に影響）。 |
| includeRecipe | bool | no | false | true の場合、`currentRecipe`（署名の元データ）も返します（デバッグ）。 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "rebar_sync_status",
  "params": {
    "useSelectionIfEmpty": true,
    "profile": "default"
  }
}
```

## 注意
- **read-only** で、ledger DataStorage を新規作成しません。
- `isInSync` は `lastSignature == currentSignature`（大文字小文字は無視）で判定します。
- 署名は、canonical JSON 化した “recipe” から SHA-256 を計算しています（host/profile/options/mapping/actions 等を含む）。

## 関連
- `rebar_regenerate_delete_recreate`
- `rebar_plan_auto`
- `rebar_apply_plan`

