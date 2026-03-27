# family.batch_add_parameter_from_folder

- Category: Family
- Kind: write
- Risk: high
- Summary: Open every `.rfa` in a folder, add family or shared parameters safely, save, and close with per-file results.

## Purpose
Use this command to batch-add parameters to offline family files in a folder.

This command is designed to be safe for production use:
- It edits `.rfa` files only.
- It does not edit `.rvt` project documents.
- It creates backups before overwrite by default.
- It records per-file audit logs in JSONL format.
- In V1, it does not forcibly replace mismatched existing parameters.

## Request
- Method: `family.batch_add_parameter_from_folder`

### Top-level parameters
| Name | Type | Required | Default | Notes |
|---|---|---|---|---|
| `folderPath` | string | yes |  | Root folder containing `.rfa` files. |
| `searchPattern` | string | no | `*.rfa` | File mask for enumeration. |
| `recursive` | bool | no | `false` | Search subfolders. |
| `dryRun` | bool | no | `false` | Validate and report without saving files. |
| `continueOnError` | bool | no | `true` | Continue processing remaining files after a failure. |
| `saveMode` | string | no | `overwrite` | `overwrite` or `save_as_copy`. |
| `outputFolder` | string | no |  | Required when `saveMode=save_as_copy`. |
| `createBackup` | bool | no | `true` | Create a backup before overwrite. |
| `backupFolder` | string | no | `<folderPath>\\_backup` | Backup destination when overwriting. |
| `closeWithoutSaveOnNoChange` | bool | no | `true` | Close unchanged families without saving. |
| `defaultSharedParameterFile` | string | no |  | Default shared parameter file for shared-mode items. |
| `defaultSharedParameterGroupName` | string | no | `Common` | Default definition group name in the shared parameter file. |
| `parameters` | array | yes |  | Parameter definitions to add. |

### `parameters[]`
| Name | Type | Required | Notes |
|---|---|---|---|
| `parameterMode` | string | yes | `shared` or `family`. |
| `parameterName` | string | yes | Target family parameter name. |
| `parameterGroup` | string | no | Revit parameter group. Default is `PG_DATA`. |
| `isInstance` | bool | no | Default is `true`. |
| `onExists` | string | no | `skip` or `error`. Default is `skip`. |
| `sharedParameterFile` | string | shared mode | Path to shared parameter file. `sharedParametersFile` is also accepted. |
| `sharedParameterGroupName` | string | shared mode | Group name inside the shared parameter file. |
| `sharedParameterDefinitionName` | string | shared mode | Explicit definition name if different from `parameterName`. |
| `sharedParameterGuid` | string | shared mode recommended | GUID-first matching is used for safety. `guid` and `sharedGuid` are also accepted. |
| `familySpecType` | string | family mode | Required when `parameterMode=family`. |

## Shared parameter file notes
- Revit-compatible shared parameter files should be used.
- In practice, UTF-16 LE with BOM is the safest encoding for Japanese content.
- If the file cannot be opened, the command returns a per-file failure such as:
  - `Shared parameter file could not be opened. Error in readParamDatabase`

## Safety behavior
- Existing matching parameters are skipped.
- Existing mismatched parameters are not destructively replaced in V1.
- Shared parameters are matched by GUID first, then by compatible name.
- Families are always closed in a safe path even when an error occurs.
- Backups are created before overwrite when `createBackup=true`.

## Request examples

### Dry-run for shared parameter add
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "family.batch_add_parameter_from_folder",
  "params": {
    "folderPath": "C:\\Users\\<you>\\Documents\\Revit_MCP\\Samples\\Family",
    "searchPattern": "*.rfa",
    "recursive": false,
    "dryRun": true,
    "saveMode": "overwrite",
    "defaultSharedParameterFile": "C:\\Users\\<you>\\Documents\\Revit_MCP\\Samples\\family_batch_test_shared_params.txt",
    "defaultSharedParameterGroupName": "Common",
    "parameters": [
      {
        "parameterMode": "shared",
        "parameterName": "ファミリパラメータ追加確認",
        "parameterGroup": "PG_DATA",
        "isInstance": true,
        "onExists": "skip",
        "sharedParameterGuid": "1e3103c5-868d-47f1-b347-0f4669cc3b7d"
      }
    ]
  }
}
```

### Actual overwrite with backup
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "family.batch_add_parameter_from_folder",
  "params": {
    "folderPath": "C:\\Users\\<you>\\Documents\\Revit_MCP\\Samples\\Family",
    "searchPattern": "*.rfa",
    "recursive": false,
    "dryRun": false,
    "continueOnError": true,
    "saveMode": "overwrite",
    "createBackup": true,
    "backupFolder": "C:\\Users\\<you>\\Documents\\Revit_MCP\\Samples\\Family__backup",
    "defaultSharedParameterFile": "C:\\Users\\<you>\\Documents\\Revit_MCP\\Samples\\family_batch_test_shared_params.txt",
    "defaultSharedParameterGroupName": "Common",
    "parameters": [
      {
        "parameterMode": "shared",
        "parameterName": "ファミリパラメータ追加確認",
        "parameterGroup": "PG_DATA",
        "isInstance": true,
        "onExists": "skip",
        "sharedParameterGuid": "1e3103c5-868d-47f1-b347-0f4669cc3b7d"
      }
    ]
  }
}
```

## Result shape
The result includes:
- `processed`
- `succeeded`
- `failed`
- `skipped`
- `dryRun`
- `logPath`
- `items[]` with per-file results

Each file item includes:
- `filePath`
- `ok`
- `action`
- `saved`
- `savePath`
- `backupPath`
- `addedCount`
- `skippedCount`
- `failedCount`
- `elapsedMs`
- `messages[]`
- `parameterResults[]`

## Logs
Audit logs are written to:
- `%USERPROFILE%\\Documents\\Revit_MCP\\Logs\\family.batch_add_parameter_from_folder\\*.jsonl`

## Related commands
- `family.query_loaded`
- `set_family_type_parameter`
- `update_family_instance_parameter`
