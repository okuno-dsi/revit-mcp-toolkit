# update_project_info

Update Revit Project Information (Project Info element) string fields safely.

- Method: `update_project_info`
- Category: Bootstrap/Project
- Kind: write

Parameters (all optional; string)
- `projectName`
- `projectNumber`
- `clientName`
- `status`
- `issueDate`
- `address`

Notes
- Only writable string parameters are updated. Missing/readonly fields are skipped without error.
- Transaction is scoped to this operation and commits only the fields that could be set.
- The command targets the active document's ProjectInformation element.

Examples
```bash
# Python (direct)
python Manuals/Scripts/send_revit_command_durable.py \
  --port 5211 \
  --command update_project_info \
  --params '{"projectName":"Test BIM Model","projectNumber":"P-001"}'

# PowerShell
pwsh -File Manuals/Scripts/send_revit_command_durable.py \
  --port 5211 --command update_project_info \
  --params '{"clientName":"ACME Corp.","status":"Design"}'
```

Response
```json
{ "ok": true, "updated": 2 }
```

Troubleshooting
- If you see `Unknown command: update_project_info`, make sure the Add‑in has been rebuilt and reloaded (restart Revit) so the command is registered.
- Some environments lock Project Info fields (e.g., via worksharing/permissions). In those cases `updated` may be 0 even if parameters are present.

