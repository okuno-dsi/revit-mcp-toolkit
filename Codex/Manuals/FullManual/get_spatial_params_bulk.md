# get_spatial_params_bulk

- Category: Spatial
- Purpose: Bulk parameter fetch for Room / Space / Area.

## Overview
This command collects parameters for Rooms, Spaces, or Areas in one request. It supports paging across elements and paging within each element's parameter list.

- If neither `kind` nor `kinds` is supplied, it defaults to `room`.
- If `elementIds` is empty, it fetches all elements in the requested kinds (or when `all=true`).
- Use `paramSkip` / `paramCount` to trim large parameter payloads.

## Usage
- Method: get_spatial_params_bulk

### Parameters
| Name | Type | Required | Description |
|---|---|---|---|
| kind | string | no | Single kind: `room`, `space`, or `area`. |
| kinds | string[] | no | Multiple kinds, e.g. `["room","space"]`. |
| elementIds | int[] | no | Target element IDs. If omitted, `all` is assumed. |
| all | bool | no | Fetch all elements of the selected kinds. |
| elementSkip | int | no | Skip elements (paging). |
| elementCount | int | no | Limit elements (paging). |
| paramSkip | int | no | Skip parameters per element. |
| paramCount | int | no | Limit parameters per element. |
| unitsMode | string | no | `SI` / `Project` / `Raw` / `Both` (default: `SI`). |

Notes:
- `skip` / `count` are accepted as aliases for `elementSkip` / `elementCount`.
- `unitsMode` affects the `parameters[]` payload values.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_spatial_params_bulk",
  "params": {
    "kind": "room",
    "all": true,
    "elementSkip": 0,
    "elementCount": 50,
    "paramSkip": 0,
    "paramCount": 30,
    "unitsMode": "SI"
  }
}
```

## Response
```jsonc
{
  "ok": true,
  "totalCount": 120,
  "elementSkip": 0,
  "elementCount": 50,
  "paramSkip": 0,
  "paramCount": 30,
  "units": { "Length": "mm", "Area": "m2", "Volume": "m3", "Angle": "deg" },
  "mode": "SI",
  "items": [
    {
      "kind": "room",
      "elementId": 6808338,
      "uniqueId": "f7b1d8a1-...-000a5f",
      "name": "事務室2-1",
      "levelId": 12345,
      "levelName": "2FL",
      "totalParams": 86,
      "parameters": [
        {
          "name": "面積",
          "id": 123456,
          "storageType": "Double",
          "isReadOnly": true,
          "dataType": "autodesk.spec.aec:area-2.0.0",
          "value": 23.45,
          "unit": "m2",
          "display": "23.45 m²",
          "raw": 252.48
        }
      ]
    }
  ]
}
```

## Related
- spatial.suggest_params
- get_room_params
- get_space_params
- get_area_params
