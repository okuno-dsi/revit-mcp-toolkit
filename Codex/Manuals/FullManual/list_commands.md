# list_commands

- Category: MetaOps
- Purpose: List all command method names currently registered in the running Revit add-in.

## Overview
Returns the set of `CommandName` strings registered in `RevitMcpWorker` for the current Revit instance.

## Usage
- Method: `list_commands`

### Parameters
None.

### Example Result (shape)
```jsonc
{
  "ok": true,
  "commands": ["get_project_info", "get_walls", "..."]
}
```

