# get_parameter_identity

- Category: ParamOps
- Purpose: Resolve a parameter on an element/type and return rich identity and metadata suitable for auditing, CSV, and automation.

## Usage
- Method: `get_parameter_identity`

### Parameters
- `target` (required): `{ "by":"elementId|typeId|uniqueId", "value": <id|string> }`
- One of (by priority): `builtInId` → `builtInName` → `guid` → `paramName`
  - Aliases supported: `name`, `builtIn`, `built_in`, `builtInParameter`, `paramGuid`, `GUID`
- `attachedToOverride` (optional): `"instance" | "type"` — prefer where to resolve first. Auto-fallback checks the other.
- `fields` (optional): array of parameter fields to project for a compact response. Example: `["name","origin","group","placement","guid"]`

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_parameter_identity",
  "params": {
    "target": { "by": "elementId", "value": 123456 },
    "builtInName": "ALL_MODEL_TYPE_IMAGE",
    "attachedToOverride": "type",
    "fields": ["name","origin","group","placement","attachedTo","guid","isReadOnly","displayUnit"]
  }
}
```

### Result Shape
```jsonc
{
  "ok": true,
  "result": {
    "found": true,
    "target": { "kind": "element|type", "id": 123456, "uniqueId": "..." },
    "resolvedBy": "builtInName:ALL_MODEL_TYPE_IMAGE",
    "parameter": {
      "name": "Type Image",
      "paramId": -1001250,
      "storageType": "ElementId",
      "origin": "builtIn | project | shared | family",
      "group": { "enumName": "PG_IDENTITY_DATA", "uiLabel": "Identity Data" },
      "placement": "instance | type",
      "attachedTo": "instance | type",
      "isReadOnly": false,
      "isShared": false,
      "isBuiltIn": true,
      "guid": null,
      "parameterElementId": 0,
      "categories": ["Doors","Windows"],
      "dataType": { "storage": "ElementId", "spec": "Autodesk.Spec:..." },
      "displayUnit": "mm|m2|m3|deg|raw",
      "allowVaryBetweenGroups": null,
      // value block respects unitsMode (default: SI)
      // - SI/Project/Raw: { display, unit, value, raw }
      // - Both          : { display, unitSi, valueSi, unitProject, valueProject, raw }
      "value": { "display": "2000 mm", "unit": "mm", "value": 2000.0, "raw": 6.56168 },
      "notes": "Likely type-level parameter; change type to edit"
    }
  }
}
```

Notes
- If resolution on the instance fails, the add-in auto-checks the type element and sets `attachedTo` accordingly.
- `origin` is inferred as `builtIn` (id<0), otherwise `shared` (ExternalDefinition/GUID), else `project`. In family docs it may be `family`.
- `group.uiLabel` is localized by Revit (`LabelUtils`).
- `fields` projects the `parameter` block for compact payloads; `found/target/resolvedBy` are always included.

## Timeouts and Chunking
- When exporting identities for many parameters, first collect a parameter id list, then resolve in chunks (e.g., 10–25 per request).
- This keeps each call under 10 seconds in typical projects and avoids client timeouts. Batch the chunks with small pauses when needed.
- GUID policy: GUID is emitted only for shared parameters. For built-in/project/family parameters, `guid` is always `null`.

## Related
- get_param_values
- get_param_meta
- get_type_parameters_bulk
- get_instance_parameters_bulk
- update_parameters_batch
