# path.waypoints.get

- Category: Route
- Purpose: Get waypoints of a `PathOfTravel` element.

## Overview
Returns the current waypoint list (if any) from a `PathOfTravel` element.

## Usage
- Method: `path.waypoints.get`
- Transaction: Read

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
  "id": "wp-get-1",
  "method": "path.waypoints.get",
  "params": { "pathId": 999888 }
}
```

## Result
- `data.waypoints[]`: points in mm (`{x,y,z}`)
- `data.count`: waypoint count

