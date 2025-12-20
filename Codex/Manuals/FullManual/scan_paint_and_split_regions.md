# scan_paint_and_split_regions

- Category: Materials
- Purpose: Scan elements and report which ones have painted faces and/or Split Face regions.

## Overview
Runs a geometry scan over selected categories (or a default set) and returns elements that have at least one painted face or at least one face with Split Face regions.  
Optionally, it can also return face-level stable references for further inspection by follow-up commands.

## Usage
- Method: scan_paint_and_split_regions

### Parameters
```jsonc
{
  "categories": ["Walls", "Floors", "Ceilings"],  // optional; any category names or BuiltInCategory names
  "includeFaceRefs": false,                       // optional, default: false
  "maxFacesPerElement": 50,                       // optional, only when includeFaceRefs = true
  "limit": 0                                      // optional; 0 or null = no element limit
}
```

- `categories` (string[], optional):
  - Logical filter for categories to scan.
  - Accepts friendly names (`"Walls"`, `"Floors"`, `"Roofs"`, `"Generic Models"`, `"構造フレーム"` etc.) or `BuiltInCategory` names (e.g. `"OST_Walls"`).
  - Names that cannot be resolved or correspond to non-model categories are silently ignored.
  - If omitted or empty, a default set of paint-relevant model categories is used (walls, floors, roofs, ceilings, generic models, columns, doors, windows, etc.).
- `includeFaceRefs` (bool, optional, default `false`):
  - `false`: return only element-level flags (`hasPaint`, `hasSplitRegions`).
  - `true`: also include a `faces[]` array with per-face info and stable references.
- `maxFacesPerElement` (int, optional, default `50`):
  - Only used when `includeFaceRefs == true`.
  - Limits the number of face entries per element to keep payload size bounded.
- `limit` (int, optional, default `0`):
  - Maximum number of elements to return.
  - `0` or `null` means “no limit”.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": "scan-paint-walls-floors",
  "method": "scan_paint_and_split_regions",
  "params": {
    "categories": ["Walls", "Floors"],
    "includeFaceRefs": true,
    "maxFacesPerElement": 20,
    "limit": 200
  }
}
```

## Result
```jsonc
{
  "ok": true,
  "msg": null,
  "items": [
    {
      "elementId": 123456,
      "uniqueId": "f8f6455a-...",
      "category": "Walls",
      "hasPaint": true,
      "hasSplitRegions": true,
      "faces": [
        {
          "stableReference": "RVT_LINK_INSTANCE:...:ELEMENT_ID:...:FACE_ID:...",
          "hasPaint": true,
          "hasRegions": true,
          "regionCount": 3
        }
      ]
    }
  ]
}
```

- `ok` (bool): `true` on success; `false` if there was an input or internal error.
- `msg` (string|null): `null` on success; otherwise an explanatory error message.
- `items` (array): Only elements that have at least one painted face or Split Face regions are included.

Each element item:
- `elementId` / `uniqueId`: Revit identifiers.
- `category`: Revit category name (if available).
- `hasPaint`: `true` if at least one face is painted.
- `hasSplitRegions`: `true` if at least one face has Split Face regions.
- `faces` (optional, only when `includeFaceRefs == true`):
  - `stableReference`: stable reference string for the face (may be `null` if not available).
  - `hasPaint`: whether this face is painted.
  - `hasRegions`: whether this face has Split Face regions.
  - `regionCount`: number of regions reported by `Face.GetRegions()` (0 on failure).

## Notes

- Categories that cannot be painted or have no solids are simply ignored (no exceptions are thrown); their elements will just not appear in `items`.
- Face-level stable references can be passed to other commands (e.g., detailed face/paint inspection) if such commands are available.
- For large projects, consider using `categories`, `limit`, and `maxFacesPerElement` to keep performance acceptable.

## Related
- get_paint_info
- apply_paint
- remove_paint

