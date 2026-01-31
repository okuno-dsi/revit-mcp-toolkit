# dynamo.list_scripts

- Category: Dynamo
- Purpose: List available Dynamo .dyn scripts from the controlled Scripts folder.

## Overview
This command enumerates `.dyn` files under `RevitMCPAddin/Dynamo/Scripts` and returns basic metadata (inputs/outputs). If a matching `ScriptMetadata/<name>.json` exists, its description/inputs override the defaults.

## Usage
- Method: dynamo.list_scripts
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "dynamo.list_scripts",
  "params": {}
}
```

## Result (high level)
- `scripts[]`: list of scripts with `name`, `fileName`, `relativePath`, `inputs`, `outputs`
- `scriptsRoot`: absolute scripts folder
- `metadataRoot`: metadata folder
- `dynamoReady`: whether Dynamo runtime was detected
- `dynamoError`: error message if not ready

### Params Schema
```json
{
  "type": "object",
  "properties": {}
}
```

### Result Schema
```json
{
  "type": "object",
  "properties": {
    "scripts": { "type": "array" },
    "scriptsRoot": { "type": "string" },
    "metadataRoot": { "type": "string" },
    "dynamoReady": { "type": "boolean" },
    "dynamoError": { "type": "string" }
  },
  "additionalProperties": true
}
```
