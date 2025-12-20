# duplicate_fire_protection_instance

- Category: FireProtection
- Purpose: Copy an existing Fire Protection instance to a new location (mm input).

## Usage
- Method: duplicate_fire_protection_instance

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | int | yes |  |
| location | object | yes |  |

`location` must be `{ "x": number, "y": number, "z": number }` in **mm**.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "duplicate_fire_protection_instance",
  "params": {
    "elementId": 123456,
    "location": { "x": 1000.0, "y": 2000.0, "z": 0.0 }
  }
}
```

## Related
- get_fire_protection_instances
- create_fire_protection_instance
- move_fire_protection_instance
- delete_fire_protection_instance

