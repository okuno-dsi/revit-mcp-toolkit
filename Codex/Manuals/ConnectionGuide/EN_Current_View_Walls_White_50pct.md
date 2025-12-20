# Make All Walls in Current View White with 50% Transparency (Revit MCP)

## Overview
This runbook sets every wall visible in the current view to White (RGB 255,255,255) with 50% transparency using the Revit MCP add-in. It applies a category-level graphic override for the Walls category, which is efficient and reliable. Notes and an element-level fallback are included.

## Prerequisites
- Revit is running with the MCP add-in active.
- MCP port is known (example: `5210`).
- You can run Python (`python`) from this repository, using `Manuals/Scripts/send_revit_command_durable.py`.

## Quick Path (Recommended): Category-Level Override
1) Get the active view ID
```bash
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command get_current_view
```
Expected: JSON where `result.viewId` (or `viewId`) is present, e.g. `{"ok":true, "viewId": 401}`.

2) Apply category override for Walls in the current view
```bash
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command set_category_override \
  --params '{
    "viewId": 401,
    "categoryIds": [-2000011],
    "color": {"r":255, "g":255, "b":255},
    "applyFill": true,
    "transparency": 50
  }'
```
Notes:
- `-2000011` is the BuiltInCategory ID for `OST_Walls`.
- Success example includes: `{"ok":true, "overridden": <n>, "appliedTo":"view"}`.
- If the view has a View Template, MCP may skip the operation safely and return `templateApplied=true` and `appliedTo:"skipped"`.

## If View Template Skips Category Override
You have two options:
- Temporarily remove or relax the View Template and re-run the category override.
- Or use element-level overrides to color each wall individually (slower).

### Element-Level Fallback (Slower)
1) Get the active view ID (same as above).

2) List visible elements in the view with paging and extract wall IDs
```powershell
$port = 5210
$python = "python"
$script = ".\Manuals/Scripts/send_revit_command_durable.py"

function Call-Py($commandName, $paramsJson){
  if ($paramsJson){ return & $python $script --port $port --command $commandName --params $paramsJson }
  else { return & $python $script --port $port --command $commandName }
}

function Get-Body($jsonText){ $o = $jsonText | ConvertFrom-Json; if ($o.result) { $o.result } else { $o } }

# 1) View ID
$viewJson = Call-Py 'get_current_view' ''
$vid = (Get-Body $viewJson).viewId

# 2) Page through get_elements_in_view
$allRows = New-Object System.Collections.Generic.List[Object]
$skip = 0; $count = 1000; $guard = 0
while ($true) {
  $p = @{ viewId = $vid; skip = $skip; count = $count } | ConvertTo-Json -Compress
  $resp = Call-Py 'get_elements_in_view' $p
  $b = Get-Body $resp
  if ($b.rows) {
    $rows = @($b.rows); if ($rows.Count -eq 0) { break }
    foreach ($r in $rows) { [void]$allRows.Add($r) }
    $skip += $count
    if ($b.totalCount -and $allRows.Count -ge $b.totalCount) { break }
  } elseif ($b.elementIds) {
    foreach ($id in $b.elementIds) { [void]$allRows.Add(@{ elementId = $id }) }
    break
  } else { throw "Unexpected shape for get_elements_in_view response." }
  if ((++$guard) -ge 100) { break }
}

# 3) Filter walls (Walls categoryId = -2000011)
$wallIds = New-Object System.Collections.Generic.List[Int32]
foreach ($row in $allRows) {
  if ($row.categoryId -eq -2000011 -or $row.categoryName -eq 'Walls' -or $row.categoryName -eq '壁') {
    if ($row.elementId) { [void]$wallIds.Add([int]$row.elementId) }
  }
}
$wallIds = [System.Linq.Enumerable]::Distinct($wallIds)

# 4) Per-element override to white / 50% transparency
foreach ($eid in $wallIds) {
  $pp = @{ viewId = $vid; elementId = $eid; r = 255; g = 255; b = 255; transparency = 50 } | ConvertTo-Json -Compress
  [void](Call-Py 'set_visual_override' $pp)
}
"Done: ${($wallIds | Measure-Object).Count} walls colored white (50% transparency)."
```

## Clearing Overrides (Optional)
- Save/restore recommended:
  - Save: `save_view_state` (optionally includeHiddenElements)
  - Restore: `restore_view_state` (apply { template/categories/filters/worksets } as needed)
- For emergency “show everything”: `show_all_in_view` (optionally detach template), then restore to previous snapshot.
- Clear per-element override: `clear_visual_override` for each element.

## Troubleshooting
- If `get_elements_in_view` returns `rows` with `totalCount`, use paging (`skip`/`count`). Some models return `elementIds` for small sets.
- If the output shows encoding issues when redirecting to files in PowerShell, prefer not to redirect, or set `chcp 65001` before running Python.


