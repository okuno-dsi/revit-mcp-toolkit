# get_room_perimeter_with_columns_and_walls

- Category: Room
- Purpose: Get room perimeter (optionally with segments) with temporary column room-bounding, and match nearby walls to boundary segments.

## Overview
This command extends `get_room_perimeter_with_columns` with:
- Segment coordinate output (optional), and
- Matching nearby walls to each boundary segment (2D geometry).

Internally it can temporarily enable **Room Bounding** on specified (or auto-detected) columns during the perimeter computation, then rolls back the change (TransactionGroup rollback).

## Usage
- Method: `get_room_perimeter_with_columns_and_walls`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| roomId | int | yes |  |
| includeSegments | bool | no | false |
| includeIslands | bool | no | true |
| boundaryLocation / boundary_location | string | no | Finish |
| useBuiltInPerimeterIfAvailable | bool | no | true |
| columnIds | int[] | no | [] |
| autoDetectColumnsInRoom | bool | no | false |
| searchMarginMm | number | no | 1000.0 |
| includeWallMatches | bool | no | true |
| wallMaxOffsetMm | number | no | 300.0 |
| wallMinOverlapMm | number | no | 100.0 |
| wallMaxAngleDeg | number | no | 3.0 |
| wallSearchMarginMm | number | no | 5000.0 |

### Example Request (with segments + wall matching)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_room_perimeter_with_columns_and_walls",
  "params": {
    "roomId": 6808124,
    "includeSegments": true,
    "includeWallMatches": true,
    "autoDetectColumnsInRoom": true,
    "searchMarginMm": 1000.0,
    "wallMaxOffsetMm": 300.0,
    "wallMinOverlapMm": 100.0,
    "wallMaxAngleDeg": 3.0
  }
}
```

## Related
- get_room_perimeter_with_columns
- find_walls_near_segments

