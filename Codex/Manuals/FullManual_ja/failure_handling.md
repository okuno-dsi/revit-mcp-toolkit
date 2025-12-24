# failureHandling（Failure whitelist による安全な失敗処理）

これは「ルータ（CommandRouter）」レベルの安全機構で、バッチ的な操作の安定性を上げるためのものです。

## いつ発動するか
コマンドが **5件以上の要素** を処理すると推定された場合（`elementIds` / `target(s)` をヒューリスティックにカウント）、かつ `failureHandling` を明示していないとき、実行前に次を返して停止することがあります:
- `code: "FAILURE_HANDLING_CONFIRMATION_REQUIRED"`
- `data.targetCount`, `data.threshold`, `data.defaultMode`
- `data.whitelist`: whitelist の読み込み状態
- `nextActions`: `failureHandling` を明示した再実行の例

注: `confirmToken` ハンドシェイクで `dryRun: true` が必要になるため、`dryRun: true` の呼び出しではこの確認ゲートをスキップします。

## 指定方法
`params` に次のいずれかを含めてください。

- オフ:
```json
{ "failureHandling": { "enabled": false } }
```

- ロールバック（推奨・既定）:
```json
{ "failureHandling": { "enabled": true, "mode": "rollback" } }
```

- 削除（whitelist に許可された警告のみ）:
```json
{ "failureHandling": { "enabled": true, "mode": "delete" } }
```

- 解決（whitelist のみ。解決できない場合はロールバック）:
```json
{ "failureHandling": { "enabled": true, "mode": "resolve" } }
```

ショートハンドも受け付けます:
- `failureHandling: true`（rollback 相当）
- `failureHandling: "delete"` / `"resolve"` / `"rollback"`

## 挙動（安全側）
- whitelist の **有効なルールに一致する failure** だけを自動処理（削除/解決）します。
- whitelist にない failure は安全のためロールバックします（無人実行で壊れた状態を残さない）。
- `delete` は whitelist で `delete_warning` が許可された警告のみ削除します。
- `resolve` は whitelist で `resolve_if_possible` が許可されたものだけ解決を試みます（best effort）。解決できない場合はロールバック（または、許可されていれば警告削除）します。
- `failureHandling` を有効にすると、処理キューがモーダルダイアログで停止しないように（best effort で）自動Dismissすることがあります。Dismissされたダイアログは `failureHandling.issues.dialogs[]`（`dismissed`, `overrideResult`）に記録されます。

## whitelist ファイル（`failure_whitelist.json`）
アドインは `failure_whitelist.json` を（best effort で）次から探します:
- `%LOCALAPPDATA%\\RevitMCP\\failure_whitelist.json`
- `%USERPROFILE%\\Documents\\Codex\\Design\\failure_whitelist.json`
- `<AddinFolder>\\Resources\\failure_whitelist.json`
- `<AddinFolder>\\failure_whitelist.json`
- （開発用）上位フォルダを辿って `...\\Codex\\Design\\failure_whitelist.json`

環境変数で上書き可能:
- `REVITMCP_FAILURE_WHITELIST_PATH`

## 応答フィールド
`failureHandling` を明示した場合、応答に次が付与されることがあります:
- `failureHandling.enabled`
- `failureHandling.mode`
- `failureHandling.whitelist`（ok/code/msg/path/version）
- `failureHandling.issues.failures[]`（捕捉した failure。例）:
  - `action`: `delete_warning`, `resolve`, `rollback_*` など
  - `whitelisted`: `true | false`
  - `ruleId`: whitelist ルールID（一致した場合）

また、**ロールバック／トランザクション未コミット**（例: `code: "TX_NOT_COMMITTED"`）が発生した場合は、`failureHandling` を明示していなくても、警告詳細を必ず記録するためにロールバック診断情報が付与されることがあります:
- `failureHandling.issues`（捕捉した failure / dialog）
- `failureHandling.rollbackDetected: true`
- `failureHandling.rollbackReason`
- `failureHandling.autoCaptured: true`（暗黙付与された場合）
