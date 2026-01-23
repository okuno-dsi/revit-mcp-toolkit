# Create Flush Walls (face-aligned to existing walls)

- Category: ElementOps
- Kind: write
- Purpose: Create new walls of a specified type, aligned (“flush”) to a chosen side/plane reference of existing walls.

Creates new walls of a specified type, aligned (“flush”) to a chosen side/plane reference of existing walls.

Typical use: create a finishing wall type (e.g. `"(内壁)W5"`) tightly attached to the **-Y** side of selected walls.

## Command
- Canonical: `element.create_flush_walls`
- Aliases: `create_flush_walls`

## Parameters
- `sourceWallIds` (optional, `int[]`): source wall element IDs. If omitted/empty, the current selection is used (walls only).
- `newWallTypeNameOrId` (required, `string`): the new `WallType` by **name** or **ElementId string**.
  - Back-compat keys (only used if `newWallTypeNameOrId` is empty): `newWallTypeName`, `wallTypeName`, `wallTypeId`.
- `sideMode` (optional, `string`, default `ByGlobalDirection`): which side to place on.
  - `ByGlobalDirection` (recommended for “-Y side”)
  - `ByExterior`
  - `ByInterior`
- `globalDirection` (optional, `double[3]`, default `[0,-1,0]`): only used when `sideMode=ByGlobalDirection`.
  - The side whose **finish-face normal** is closest to this direction is chosen (XY only).
- `sourcePlane` (optional, `string`, default `FinishFace`): plane reference on the **source wall** (contact side).
  - `FinishFace` / `CoreFace` / `WallCenterline` / `CoreCenterline`
- `newPlane` (optional, `string`, default empty): plane reference on the **new wall** (side facing the source). If empty, uses `sourcePlane`.
- `newExteriorMode` (optional, `string`, default `MatchSourceExterior`): how to orient the new wall’s exterior direction.
  - `AwayFromSource` / `MatchSourceExterior` / `OppositeSourceExterior`
- `miterJoints` (optional, `bool`, default `true`): best-effort miter for connected chains (Line-Line only).
- `copyVerticalConstraints` (optional, `bool`, default `true`): copies base/top constraints (levels, offsets, and height).
  - Note: **Attach Top/Base** relationships are not copied (Revit 2023 limitation).

## Example (JSON-RPC)
Create walls flush to the **global -Y** side of currently selected walls:
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "element.create_flush_walls",
  "params": {
    "newWallTypeNameOrId": "(内壁)W5",
    "sideMode": "ByGlobalDirection",
    "globalDirection": [0, -1, 0],
    "sourcePlane": "FinishFace",
    "newPlane": "FinishFace",
    "newExteriorMode": "MatchSourceExterior",
    "miterJoints": true,
    "copyVerticalConstraints": true
  }
}
```

## Result (shape)
- `ok`, `msg`
- `createdWallIds` (`int[]`)
- `warnings` (`string[]`)

## Notes
- Alignment is computed from the source wall’s `LocationCurve` (centerline) and `CompoundStructure` layer widths (more stable than face-based offsets).
- Walls without a `LocationCurve` are skipped and reported via `warnings`.
