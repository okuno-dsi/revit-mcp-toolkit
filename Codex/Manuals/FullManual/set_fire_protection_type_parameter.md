# set_fire_protection_type_parameter

- Category: FireProtection
- Purpose: Set a Fire Protection type parameter (FamilySymbol) by name/built-in/guid.

## Usage
- Method: set_fire_protection_type_parameter

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| typeId | int | yes |  |
| paramName | string | no / one of |  |
| builtInName | string | no / one of |  |
| builtInId | int | no / one of |  |
| guid | string | no / one of |  |
| value | any | yes |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_fire_protection_type_parameter",
  "params": {
    "typeId": 12345,
    "paramName": "Comments",
    "value": "test"
  }
}
```

## Related
- get_fire_protection_type_parameters

