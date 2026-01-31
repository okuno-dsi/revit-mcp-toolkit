# dynamo.run_script

- Category: Dynamo
- Purpose: Execute a Dynamo graph (.dyn) with input overrides from MCP.

## Overview
This command runs a Dynamo graph inside the Revit process. The script must exist under `RevitMCPAddin/Dynamo/Scripts`. Inputs are matched by `Name` or `Id` from the `.dyn` `Inputs` section. The add-in writes a temporary run copy under `%LOCALAPPDATA%\\RevitMCP\\dynamo\\runs`.

## Usage
- Method: dynamo.run_script
- Parameters:
  - `script` (string, required): file name or relative path under Scripts (e.g., `move_elements` or `subdir/move_elements.dyn`).
  - `inputs` (object, optional): input overrides by name or Id.
  - `timeoutMs` (int, optional): wait time for evaluation completion (default 120000).
  - `showUi` (bool, optional): show Dynamo UI during run (default false).
  - `forceManualRun` (bool, optional): force manual run mode (default false).
  - `checkExisting` (bool, optional): reuse existing Dynamo model if possible (default true).
  - `shutdownModel` (bool, optional): request Dynamo model shutdown after run (default false).
- `hardKillRevit` (bool, optional): force-terminate Revit after run (default false).
- `hardKillDelayMs` (int, optional): delay before force-terminate (default 5000, min 1000, max 600000).

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "dynamo.run_script",
  "params": {
    "script": "move_elements.dyn",
    "inputs": {
      "element_ids": [12345, 67890],
      "offset_mm": [1000, 0, 0]
    }
  }
}
```

## Notes
- Inputs are stringified before passing to Dynamo. For list/point inputs, use JSON arrays; the graph should parse as needed.
- Outputs are best-effort and may be empty if the Dynamo model is not accessible.
- `hardKillRevit=true` will attempt (1) workspace snapshot, (2) workshared sync (if applicable), (3) save (with UI-thread retry if blocked by an open transaction), (4) restart Revit + reopen the same file, then force-terminate after the delay.
- If any of the above steps fail, Revit still force-terminates. Check add-in logs for details.
- On hard-kill, the add-in stops the MCP server and logs a stop-confirmation check (lock/process status) before terminating Revit.

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "script": { "type": "string" },
    "inputs": { "type": "object" },
    "timeoutMs": { "type": "integer" },
    "showUi": { "type": "boolean" },
    "forceManualRun": { "type": "boolean" },
    "checkExisting": { "type": "boolean" },
    "shutdownModel": { "type": "boolean" },
    "hardKillRevit": { "type": "boolean" },
    "hardKillDelayMs": { "type": "integer" }
  }
}
```

### Result Schema
```json
{
  "type": "object",
  "properties": {
    "result": { "type": "object" }
  },
  "additionalProperties": true
}
```
