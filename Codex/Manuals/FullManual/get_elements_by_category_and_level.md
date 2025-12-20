# get_elements_by_category_and_level

- Category: Selection/Query
- Purpose: Get elements of a single Revit category on a specified level.

## Overview
This command returns element IDs for a **single category** (e.g., Walls, Floors, Structural Framing) on a **single level** (by name or by elementId).  
It is optimized for AI agents to quickly get a focused set of elements, then pass those IDs to other commands (e.g., `update_wall_parameter`, `move_structural_frame`).

## Usage
- Method: get_elements_by_category_and_level

### Parameters
```jsonc
{
  "category": "Walls",
  "levelSelector": {
    "levelName": "Level 3"
    // or: "levelId": 123456
  },
  "options": {
    "includeTypeInfo": true,
    "includeCategoryName": true,
    "maxResults": 0
  }
}
```

- `category` (string, required):
  - Logical category name or BuiltInCategory enum name.
  - Examples:
    - `"Walls"` → `OST_Walls`
    - `"Floors"` → `OST_Floors`
    - `"Structural Framing"` → `OST_StructuralFraming`
    - `"Rooms"` → `OST_Rooms`
    - `"Spaces"` → `OST_MEPSpaces`
    - `"Doors"` → `OST_Doors`
    - `"Windows"` → `OST_Windows`
    - `"OST_Walls"` (direct enum name)

- `levelSelector` (object, required):
  - **Recommended:** `levelId` (int) – Revit elementId of the `Level` element.  
    Level names are often ambiguous or easy to mistype; when possible, resolve the `Level` once (e.g. via `get_levels` or another helper) and reuse its `elementId` here.
  - Alternatively, for interactive/manual use you may specify:
    - `levelName` (string) – e.g. `"Level 3"`, `"3F"`, `"３階"`  
      - First tries exact match (case sensitive), then falls back to case-insensitive.

- `options` (object, optional):
  - `includeTypeInfo` (bool, default: false)  
    - When true, `typeId` is included for each element.
  - `includeCategoryName` (bool, default: true)  
    - When true, `categoryName` is included.
  - `maxResults` (int, default: 0 = no explicit limit)  
    - When > 0, stops after this many matches (useful to avoid huge payloads).

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": "walls-level3",
  "method": "get_elements_by_category_and_level",
  "params": {
    "category": "Walls",
    "levelSelector": {
      "levelName": "Level 3"
    },
    "options": {
      "includeTypeInfo": true,
      "includeCategoryName": true,
      "maxResults": 0
    }
  }
}
```

## Result

### Success
```jsonc
{
  "ok": true,
  "msg": "Found 128 elements in category 'Walls' on level 'Level 3'.",
  "items": [
    {
      "elementId": 456789,
      "uniqueId": "d4e55c1a-3f58-4d16-9c35-...",
      "categoryName": "Walls",
      "levelId": 111,
      "levelName": "Level 3",
      "typeId": 2233        // when includeTypeInfo = true
    }
  ],
  "debug": {
    "category": "Walls",
    "levelResolvedBy": "NameExact",
    "totalCandidates": 989,
    "filteredCount": 128
  }
}
```

### Level not found
```json
{
  "ok": false,
  "msg": "Level not found. levelName='3F' levelId=null",
  "items": []
}
```

### Missing parameters
```json
{
  "ok": false,
  "msg": "levelSelector.levelName または levelSelector.levelId のいずれかが必要です。",
  "items": []
}
```

## Notes

- This command is intentionally limited to **a single category per call** for simplicity and predictable behavior.  
  If you need multiple categories (e.g., Walls + Floors), call this command multiple times and merge the results on the client side.
- The `debug` object can be used to understand how the command resolved the level and how many elements were filtered.
- For very large models, consider using `maxResults` together with existing paging patterns on the client side to avoid timeouts.

## Related
- get_walls
- summarize_rooms_by_level
- update_wall_parameter
- get_rooms
