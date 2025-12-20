# Command: get_types_in_view

Returns unique ElementType ids used by elements visible in a given view. Designed to avoid per‑element get_element_info when you only need typeIds for bulk operations.

- Category: Views/Graphics
- Kind: read
- Importance: high

Inputs (JSON)
- `viewId` (int, required)
- `categories` or `categoryIds` (int[], optional): prefilter by BuiltInCategory ids
- `includeIndependentTags` (bool, default false)
- `includeElementTypes` (bool, default false)
- `modelOnly` (bool, default true): exclude non‑model (annotations/imports)
- `includeTypeInfo` (bool, default true): include `typeName`/`familyName`
- `includeCounts` (bool, default true): include per‑type instance count

Output
- `{ ok, totalCount, types: [{ typeId, count?, categoryId?, typeName?, familyName? }] }`

Example
```json
{
  "viewId": 5133538,
  "categories": [-2001320],
  "includeCounts": true,
  "includeTypeInfo": true
}
```

Typical Use
- Pair with `get_type_parameters_bulk` to fetch H/B/tw/tf for all types present in the active view without iterating instances.

