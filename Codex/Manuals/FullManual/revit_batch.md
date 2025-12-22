# revit.batch

- Category: Meta
- Purpose: Execute multiple commands in a single Revit ExternalEvent to reduce RPC round trips and overhead.

## Overview
- Canonical: `revit.batch`
- Legacy alias: `revit_batch`

This command executes `ops[]` sequentially and returns per-op results in one response.

## Usage
- Method: `revit.batch`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| ops | array | yes |  |
| transaction | string | no | `single` |
| dryRun | boolean | no | false |
| stopOnError | boolean | no | true |

`transaction`:
- `single`: Wrap all ops in one `TransactionGroup`. If any op fails (or `dryRun=true`), the group is rolled back.
- `perOp`: Wrap each op in its own `TransactionGroup`. Failed ops are rolled back; successful ops are committed (unless `dryRun=true`).
- `none`: No transaction grouping. (`dryRun=true` is rejected.)

Notes / constraints:
- `dryRun=true` requires `transaction=single` or `perOp`.
- Nested `revit.batch` inside `ops[]` is rejected.
- When `transaction != "none"`, switching documents inside ops (via `params.documentPath`) is rejected.
- Batch counts as a single command for the MCP Ledger; sub-ops are executed without per-op ledger updates.
- Some commands may have non-transactional side effects (e.g., view activation) that cannot be rolled back.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "revit.batch",
  "params": {
    "transaction": "single",
    "dryRun": true,
    "ops": [
      { "method": "doc.get_project_info", "params": {} },
      { "method": "view.get_current_view", "params": {} }
    ]
  }
}
```

### Example Result (shape)
```jsonc
{
  "ok": true,
  "code": "OK",
  "data": {
    "transaction": "single",
    "dryRun": true,
    "rolledBack": true,
    "opCount": 2,
    "okCount": 2,
    "failCount": 0,
    "results": [
      { "index": 0, "method": "doc.get_project_info", "result": { "ok": true, "code": "OK", "data": {} } },
      { "index": 1, "method": "view.get_current_view", "result": { "ok": true, "code": "OK", "data": {} } }
    ]
  }
}
```

## Related
- list_commands
- help.search_commands
- help.describe_command

