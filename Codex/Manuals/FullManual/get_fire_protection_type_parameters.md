# get_fire_protection_type_parameters

- Category: FireProtection
- Purpose: Get parameters for a Fire Protection FamilySymbol (type) with unit mapping.

## Usage
- Method: get_fire_protection_type_parameters

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| typeId | int | yes |  |
| includeDisplay | bool | no | true |
| includeRaw | bool | no | true |
| includeUnit | bool | no | true |
| siDigits | int | no | 3 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_fire_protection_type_parameters",
  "params": {
    "typeId": 12345,
    "includeDisplay": true,
    "includeRaw": true,
    "includeUnit": true,
    "siDigits": 3
  }
}
```

## Related
- get_fire_protection_types
- set_fire_protection_type_parameter

