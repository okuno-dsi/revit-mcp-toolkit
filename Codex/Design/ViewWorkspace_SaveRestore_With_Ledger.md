# View Workspace Save/Restore Design (with MCP Ledger & Authenticity Tracking)

## Overview

This document specifies a **View Workspace Save/Restore** feature for **RevitMCPAddin** (Revit .NET Framework 4.8) that can be invoked either by:
- **Ribbon/UI** (human-driven), and/or
- **MCP JSON-RPC** (agent-driven),

while preventing cross-project confusion by integrating the existing **MCP Ledger & Authenticity Tracking** mechanism that stores a **project identifier (DocKey)** in **Revit `DataStorage`**.

The feature targets **view state only**:
- Restore the set of open views (best-effort; window layout is not restorable via Revit API)
- Restore the active view
- Restore per-view zoom rectangle (2D views) when possible
- Restore 3D camera orientation (3D views) when possible
- Optional: automatic periodic snapshots (crash resilience)

---

## Goals

1. **Reliable project discrimination**
   - Every snapshot is bound to a stable **DocKey** stored in `DataStorage` (Ledger).
   - Restore is rejected if the DocKey does not match.

2. **Usable for both humans and agents**
   - Human UI: one-click save/restore.
   - MCP: explicit `doc_key` targeting supported.

3. **Crash resilience**
   - Optional autosave every N minutes.
   - Always save on `DocumentClosing` / `ApplicationClosing` (external file sink).

4. **Non-invasive**
   - Default storage is an **external JSON file**.
   - Optional storage in `DataStorage` is supported but not required.

---

## Non-Goals / Known Limits

- Recreating **window docking, tab ordering, split panes, monitor placement** is **not feasible** with standard Revit API.
- Some view types do not support zoom corner APIs; zoom restore is best-effort.
- Some view states depend on transient UI settings and may not fully restore.

---

## System Context

### Components

- **RevitMCPAddin** (in-process, .NET Framework 4.8)
  - Captures and restores view states
  - Reads DocKey from Ledger DataStorage
  - Writes snapshots to file (and optionally to DataStorage)

- **RevitMcpServer** (out-of-process, .NET 6/8)
  - Routes JSON-RPC requests
  - Does not host Revit API
  - Requests are executed in Revit via the add-in’s worker / external event pattern

### Assumptions

- A **Ledger DataStorage** entry exists per document and contains:
  - `doc_key` (stable project identifier)
  - optional: `last_command_id`, `last_snapshot_id`, etc.

---

## Data Model

### DocKey (from Ledger)

- `doc_key`: string (GUID-like or hash-like), **unique per Revit project** and **stable across sessions**.
- Source of truth: **Revit DataStorage Ledger**.
- The snapshot file name and restore target verification are based on this key.

### Snapshot (View Workspace)

Snapshot schema version: `1.0`

```json
{
  "schema_version": "1.0",
  "saved_at_utc": "2025-12-17T00:00:00Z",
  "doc_key": "DOC-KEY-FROM-LEDGER",
  "doc_title": "Project.rvt",
  "doc_path_hint": "C:\\...\\Project.rvt",
  "active_view_unique_id": "....",
  "open_views": [
    {
      "view_unique_id": "....",
      "view_id_int": 123456,
      "view_name": "Level 1",
      "view_type": "FloorPlan",
      "zoom": {
        "corner1": {"x": 0.0, "y": 0.0, "z": 0.0},
        "corner2": {"x": 10.0, "y": 10.0, "z": 0.0}
      },
      "orientation3d": null
    },
    {
      "view_unique_id": "....",
      "view_id_int": 777777,
      "view_name": "{3D}",
      "view_type": "ThreeD",
      "zoom": null,
      "orientation3d": {
        "eye": {"x": 1, "y": 2, "z": 3},
        "up": {"x": 0, "y": 0, "z": 1},
        "forward": {"x": 0.2, "y": 0.7, "z": -0.6},
        "is_perspective": true
      }
    }
  ],
  "ledger": {
    "last_command_id": "optional",
    "authenticity_token": "optional"
  }
}
```

#### Notes
- `view_unique_id` is preferred for stable lookup; `view_id_int` is a hint.
- Zoom is captured per `UIView` if supported; 3D orientation is captured per `View3D`.
- `ledger` block is optional and may be used to attach authenticity metadata (see below).

---

## Storage Strategy

### Primary Sink: External JSON File

Path example (Windows user profile):
- `%APPDATA%\RevitMCP\ViewWorkspace\workspace_{doc_key}.json`

Why:
- Works even during Revit shutdown events (no transactions required).
- Survives crashes and can be managed/archived externally.
- Avoids bloating model storage.

### Optional Sink: DataStorage (inside RVT)

If enabled:
- Store **the latest snapshot** (or a small ring buffer) in DataStorage.
- Recommended only when external IO is restricted.

Constraints:
- Requires careful size management.
- Must follow transaction rules.

---

## Authenticity & Mis-Target Prevention

### Verification Rules (Restore)

Restore may proceed only if:

1. **Ledger DocKey exists** in the current document, and
2. Snapshot `doc_key` equals current document `doc_key`.

If mismatch:
- Return `ok:false` with a clear message:
  - `"DocKey mismatch. Snapshot belongs to a different project."`

### Optional: Authenticity Token Binding

If your Ledger mechanism tracks command IDs / monotonic counters:
- Attach `last_command_id` into snapshot on save.
- On restore, compare to current ledger counter for sanity checks:
  - Accept if snapshot counter is **<= current**
  - Reject if snapshot claims a counter **> current** (corrupt or wrong file)

This is optional, and should not block basic restore unless you require strict mode.

---

## Capture Algorithm

### Inputs
- `UIApplication uiapp`
- `Document doc = uiapp.ActiveUIDocument.Document`
- `doc_key` from Ledger DataStorage

### Steps
1. Resolve `doc_key` from Ledger DataStorage.
2. Enumerate open UI views:
   - `UIDocument.GetOpenUIViews()`
3. For each `UIView`:
   - Resolve `View view = doc.GetElement(uiv.ViewId) as View`
   - Skip templates and invalid views
   - Try `uiv.GetZoomCorners()` → store corners if available
   - If `View3D`: try `GetOrientation()` → store camera orientation
4. Store `ActiveView.UniqueId`
5. Serialize to JSON and write to storage sink(s).

---

## Restore Algorithm (Stepwise Idling Execution)

Restoring views and zoom/camera may fail if applied immediately after opening.
Therefore, restore runs as a **state machine** driven by `UIApplication.Idling`:

### Phases per View
- **Phase A**: Open/activate view (`RequestViewChange`)
- **Phase B**: Apply zoom rectangle or 3D camera
- Move to next view

### Final Step
- Activate the saved `active_view_unique_id` if possible.

### Failure Handling
- Missing views (deleted/renamed): skip with warning.
- Unsupported zoom/camera: skip silently or record in result.

---

## Autosave Strategy

### Requirements
- Autosave every `N` minutes without violating Revit threading constraints.

### Implementation Pattern
- Use `System.Timers.Timer` to set a **flag only**.
- On `UIApplication.Idling`, if flag is set:
  - Capture snapshot
  - Write to file
  - Clear flag

### Default Policy
- Disabled by default (opt-in).
- Suggested default interval: **5 minutes**.
- Max snapshots retained:
  - Keep `latest` + optional ring buffer (e.g., last 10), configurable.

---

## Shutdown Capture

### DocumentClosing
- Capture and write to **external file**.
- No transaction required for file IO.

### ApplicationClosing
- Capture and write to **external file** only.
- Avoid any model modification.

---

## MCP JSON-RPC Interface

All commands return a JSON object with:
- `ok: boolean`
- `msg?: string`
- Additional fields as needed

### 1) `save_view_workspace`

**Params**
```json
{
  "doc_key": "optional; if supplied must match current doc",
  "sink": "file|datastorage|both (default file)",
  "include_zoom": true,
  "include_3d_orientation": true
}
```

**Behavior**
- Resolve current `doc_key` from Ledger.
- If param `doc_key` exists and differs → reject.
- Capture and save.

### 2) `restore_view_workspace`

**Params**
```json
{
  "doc_key": "required for MCP usage; optional for UI usage",
  "source": "file|datastorage|auto (default auto)",
  "strict_doc_key": true
}
```

**Behavior**
- Validate doc_key against Ledger.
- Load snapshot from selected source.
- Restore stepwise via Idling coordinator.

### 3) `set_view_workspace_autosave`

**Params**
```json
{
  "enabled": true,
  "interval_minutes": 5,
  "retention": 10
}
```

---

## Add-in UI (Optional)

Ribbon panel:
- **Save Workspace**
- **Restore Workspace**
- **Autosave Toggle**
- **Autosave Interval** (simple dialog)

UI usage should not require `doc_key`, since the active document is explicit, but it should still write snapshots bound to the ledger DocKey.

---

## Logging & Diagnostics

Log file location (recommended):
- `<AddinFolder>\Logs\revitmcp_view_workspace.log`
- Rotate per day or per session.

Log events:
- Save success/failure (doc_key, view count)
- Restore start/end + per-view warnings
- Autosave triggers
- DocKey mismatch errors

Return messages should be concise and agent-friendly:
- `"ok": false, "msg": "DocKey mismatch..."`

---

## Error Handling Policy

### Typical Errors
- No active document
- Ledger DocKey missing (ledger not initialized)
- Snapshot not found
- Snapshot JSON corrupt
- View missing/deleted
- Zoom/camera not supported

### Response Examples

**Ledger missing**
```json
{ "ok": false, "msg": "Ledger DocKey not found. Initialize MCP Ledger for this project first." }
```

**Wrong project**
```json
{ "ok": false, "msg": "DocKey mismatch. Snapshot belongs to a different project." }
```

---

## Compatibility Notes (Revit 2024 → 2025)

- The design uses stable, long-lived UI/document APIs.
- Any API signature differences should be isolated behind:
  - `ViewWorkspaceCapture`
  - `ViewWorkspaceRestoreCoordinator`
  - `LedgerDocKeyProvider`

---

## Implementation Plan

### Step 1: Ledger DocKey Provider
- `LedgerDocKeyProvider.GetOrNull(Document doc)`
- Returns `doc_key` (string) from DataStorage.

### Step 2: File Store
- `ViewWorkspaceStore.Save(doc_key, snapshot)`
- `ViewWorkspaceStore.Load(doc_key)`

### Step 3: Capture
- `ViewWorkspaceCapture.Capture(uiapp, doc_key, options)`

### Step 4: Restore Coordinator
- `ViewWorkspaceRestoreCoordinator.Start(snapshot)`

### Step 5: Autosave Service
- Single instance per Revit session.
- Flag + Idling execution.

### Step 6: RPC Commands + Optional Ribbon Buttons
- Register handlers for:
  - `save_view_workspace`
  - `restore_view_workspace`
  - `set_view_workspace_autosave`

---

## Testing Checklist

1. **Single project**
   - Open 3 views, set zoom, save, close Revit, reopen, restore.
2. **Multiple projects**
   - Open A and B, save A, activate B, attempt restore A in B → must reject.
3. **Deleted view**
   - Save, delete one view, restore → should skip gracefully.
4. **3D view**
   - Save camera, restore camera.
5. **Autosave**
   - Enable autosave, modify zoom, wait interval, verify file updates.
6. **Shutdown**
   - Close document and verify snapshot exists even if autosave is off.
7. **Crash recovery simulation**
   - Force close Revit (task kill), reopen, restore latest snapshot.

---

## Security / Privacy

- No personal data is required.
- Snapshot files should not include usernames or machine identifiers unless explicitly required.
- If the environment requires controlled locations, make the storage path configurable.

---

## Appendix: Recommended Folder Layout

```
RevitMCPAddin/
├── Core/
│   ├── Ledger/
│   │   └── LedgerDocKeyProvider.cs
│   ├── ViewWorkspace/
│   │   ├── ViewWorkspaceCapture.cs
│   │   ├── ViewWorkspaceRestoreCoordinator.cs
│   │   ├── ViewWorkspaceStore.cs
│   │   └── ViewWorkspaceAutoSaver.cs
├── Commands/
│   └── ViewOps/
│       ├── SaveViewWorkspaceCommand.cs
│       ├── RestoreViewWorkspaceCommand.cs
│       └── SetViewWorkspaceAutosaveCommand.cs
└── Models/
    └── ViewOps/
        └── ViewWorkspaceModels.cs
```
