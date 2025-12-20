# rename_floor_types_by_thickness

- Category: ElementOps
- Purpose: Rename FloorType names by prefixing the overall structure thickness (mm).

## Usage
- Method: `rename_floor_types_by_thickness`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| dryRun | bool | no | false |
| template | string | no | ({mm}mm)  |

`template` supports `{mm}` placeholder.

### Example Request (dry run)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "rename_floor_types_by_thickness",
  "params": {
    "dryRun": true,
    "template": "({mm}mm) "
  }
}
```

## Related
- rename_types_bulk
- rename_types_by_parameter

