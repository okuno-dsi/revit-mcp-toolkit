# get_mcp_ledger_summary

- Category: MetaOps
- Purpose: Read (and optionally create) the MCP Ledger stored in Revit `DataStorage`.

## Usage
- Method: `get_mcp_ledger_summary`

### Parameters
```jsonc
{
  "createIfMissing": true // optional (default: true)
}
```

## Notes
- The ledger is used to bind snapshots/state to the correct project/document.

