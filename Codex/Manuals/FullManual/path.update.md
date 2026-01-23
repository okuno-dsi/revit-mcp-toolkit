# path.update

- Category: Route
- Purpose: Recalculate an existing `PathOfTravel` element.

## Usage
- Method: `path.update`
- Transaction: Write

### Parameters
| Name | Type | Required | Notes |
|---|---|---:|---|
| pathId | int | yes | `PathOfTravel` element id |

Notes:
- `elementId` is also accepted as an alias for `pathId`.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": "path-update-1",
  "method": "path.update",
  "params": { "pathId": 999888 }
}
```

## Result
- `data.status`: string (`PathOfTravelCalculationStatus`)

