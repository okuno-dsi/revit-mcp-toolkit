# get_view_parameters

- Category: ViewOps
- Purpose: Get view instance parameters (and optionally view type parameters) in a spec-aware way (mm/deg).

## Usage
- Method: `get_view_parameters`

### Parameters
```jsonc
{
  "viewId": 0,                // optional (or uniqueId); default: active view
  "uniqueId": null,           // optional
  "names": ["Name", "Scale"], // optional filter (case-insensitive)
  "includeTypeParams": false, // optional (default: false)
  "includeEmpty": true        // optional (default: true)
}
```

