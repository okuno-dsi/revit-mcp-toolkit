# failureHandling (whitelist-based Revit failure handling)

This is a router-level safety feature for batch operations.

## When it triggers
If a command appears to process **5 or more target elements** (heuristic based on `elementIds` / `target(s)`), and you did not explicitly specify `failureHandling`, the router may refuse to run and return:
- `code: "FAILURE_HANDLING_CONFIRMATION_REQUIRED"`
- `data.targetCount`, `data.threshold`, `data.defaultMode`
- `data.whitelist`: whitelist load status
- `nextActions`: example patches to re-run the command with an explicit mode

Note: this confirmation gate is skipped for `dryRun: true` to keep the `confirmToken` workflow usable.

## How to specify
Send one of the following in `params`:

- Off:
```json
{ "failureHandling": { "enabled": false } }
```

- Rollback (recommended default):
```json
{ "failureHandling": { "enabled": true, "mode": "rollback" } }
```

- Delete (whitelisted warnings only):
```json
{ "failureHandling": { "enabled": true, "mode": "delete" } }
```

- Resolve (whitelist only; fallback rollback when unresolved):
```json
{ "failureHandling": { "enabled": true, "mode": "resolve" } }
```

Shorthand accepted:
- `failureHandling: true` (same as rollback)
- `failureHandling: "delete"` / `"resolve"` / `"rollback"`

## Behavior (conservative by default)
- Only failures matched by an **enabled whitelist rule** may be auto-deleted/resolved.
- Any non-whitelisted failure triggers rollback (to avoid unattended model corruption).
- In `delete` mode, only whitelist rules that allow `delete_warning` are deleted.
- In `resolve` mode, only whitelist rules that allow `resolve_if_possible` are resolved (best effort); if not resolved, rollback (or delete warning when allowed).
- When `failureHandling` is enabled, Revit modal dialogs may be auto-dismissed (best effort) to avoid blocking automation; dismissed dialogs are recorded in `failureHandling.issues.dialogs[]` (`dismissed`, `overrideResult`).

## Whitelist file (`failure_whitelist.json`)
The add-in loads `failure_whitelist.json` (best effort) from:
- `%LOCALAPPDATA%\\RevitMCP\\failure_whitelist.json`
- `%USERPROFILE%\\Documents\\Codex\\Design\\failure_whitelist.json`
- `<AddinFolder>\\Resources\\failure_whitelist.json`
- `<AddinFolder>\\failure_whitelist.json`
- (dev) walk up to find `...\\Codex\\Design\\failure_whitelist.json`

You can override via env var:
- `REVITMCP_FAILURE_WHITELIST_PATH`

## Response fields
When you explicitly pass `failureHandling`, responses may include:
- `failureHandling.enabled`
- `failureHandling.mode`
- `failureHandling.whitelist`: load status (ok/code/msg/path/version)
- `failureHandling.issues.failures[]`: captured failures including:
  - `action`: `delete_warning`, `resolve`, `rollback_*`, etc.
  - `whitelisted`: `true | false`
  - `ruleId`: whitelist rule id (if matched)

Additionally, when a command results in **rollback / transaction not committed** (e.g. `code: "TX_NOT_COMMITTED"`), the router may attach rollback diagnostics even if `failureHandling` was not explicitly provided, so warning details are always recorded:
- `failureHandling.issues` (captured failures/dialogs)
- `failureHandling.rollbackDetected: true`
- `failureHandling.rollbackReason`
- `failureHandling.autoCaptured: true` (when attached implicitly)
