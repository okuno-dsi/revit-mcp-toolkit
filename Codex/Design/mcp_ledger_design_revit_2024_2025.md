# MCP Ledger & Authenticity Tracking Design

## Target Environment
- **Current**: Autodesk Revit 2024 (primary)
- **Legacy**: Revit 2023 (not required, best-effort only)
- **Future**: Revit 2025 (migration expected within ~6 months)
- **Add-in**: MCP-based Revit automation add-in

This document defines a **robust, low-complexity authenticity and continuity mechanism** for MCP command execution using **DataStorage (ExtensibleStorage)**, with optional logging and ribbon-based ON/OFF control.

---

## 1. Design Goals

1. Guarantee **project authenticity** for MCP command execution
2. Prevent execution on the **wrong Revit project or state**
3. Support **crash recovery and deferred continuation** (e.g., night batch)
4. Minimize performance impact (always-on capable)
5. Avoid complex external dependencies (DB, file system, cloud)
6. Ensure **forward compatibility** with Revit 2025

---

## 2. Core Concept

The Revit project itself becomes the **single source of truth**.

- A **single DataStorage element** acts as an internal ledger
- MCP writes execution stamps **inside the model**
- Authenticity is verified **before every command**

The system is split into:

- **Visible summary** (optional shared parameters)
- **Authoritative ledger** (DataStorage, always authoritative)

---

## 3. Ledger Scope & Lifetime

- Exactly **one DataStorage element per project**
- Created on first MCP execution
- Never deleted automatically
- Survives:
  - Revit crash
  - Save / reopen
  - Central / local workflows

---

## 4. DataStorage Schema (Stable Contract)

```json
McpLedger {
  ProjectToken: string,        // GUID, generated once
  Sequence: number,            // Monotonic command counter

  LastCommand: {
    CommandId: string,         // GUID
    Name: string,
    ExecutedUtc: string,       // ISO-8601
    Result: "OK" | "ERROR"
  },

  ActiveSession: {
    SessionId: string,         // GUID
    Mode: "Interactive" | "Batch",
    StartedUtc: string
  } | null,

  CommandLog: [                // Optional
    {
      Seq: number,
      CommandId: string,
      Name: string,
      ParamsHash: string,
      ExecutedUtc: string,
      Result: string
    }
  ]
}
```

### Stability Rule
- **Schema shape must not change** between Revit 2024 and 2025
- New fields may be added only as optional

---

## 5. Authenticity Rules (Hard Constraints)

Before executing any MCP command:

1. Ledger **must exist**
2. `ProjectToken` must match expected project identity
3. `Sequence` must match the expected next value
4. If `ActiveSession` exists:
   - Session must be compatible, or
   - Execution must stop (Fail Closed)

If any rule fails → **command is aborted**

---

## 6. MCP Execution Flow

```
Start MCP Command
↓
Read Ledger (DataStorage)
↓
Verify ProjectToken & Sequence
↓
Open Revit Transaction
↓
Execute Revit API operations
↓
Update Ledger (same transaction)
  - Sequence++
  - LastCommand
  - Optional CommandLog append
↓
Commit Transaction
```

### Atomicity Guarantee
- Model changes and ledger update are committed **together**
- No partial state is allowed

---

## 7. Tracking Options (Runtime Config)

Tracking is **optional but enabled by default**.

```text
McpTrackingOptions
- EnableLedger        (default: true)
- EnableCommandLog    (default: true)
- MaxLogEntries       (default: 200)
```

- Options are stored in **addin settings**, not in the model
- Toggling options never breaks authenticity checks

---

## 8. Ribbon UI Design

### MCP Tab → Tracking Panel

- ☑ Enable MCP Ledger
- ☑ Record Command History
- ▶ View Ledger Summary

### UX Rules
- Default: **ON**
- Disabling shows confirmation dialog
- Even when logging is OFF:
  - `ProjectToken`
  - `Sequence`
  - `LastCommand`
  are always updated

---

## 9. Performance Characteristics

| Aspect | Impact |
|------|--------|
| DataStorage count | 1 element |
| Read per command | O(1), few ms |
| Write per command | O(1), few ms |
| Model size growth | Negligible |

Safe for **always-on usage**.

---

## 10. Revit 2024 → 2025 Compatibility

### Safe APIs
- `Autodesk.Revit.DB.DataStorage`
- `ExtensibleStorage.Schema`
- `Entity.Get<T>() / Set<T>()`

These APIs are **stable across versions**.

### Migration Strategy
- No migration required
- Existing DataStorage is read as-is
- Optional schema extension allowed

---

## 11. Failure & Recovery Scenarios

### Crash During Command
- Transaction not committed → ledger unchanged
- Safe to retry

### Crash After Commit
- Ledger reflects completed command
- No duplicate execution

### Wrong Project Opened
- `ProjectToken` mismatch → execution blocked

---

## 12. Explicit Non-Goals

- Full undo/redo reconstruction
- External audit logging
- Cross-project ledger merging

---

## 13. Summary

This design:
- Anchors MCP execution to **project reality**
- Prevents silent misapplication of commands
- Survives crashes and delayed continuation
- Requires minimal code and no infrastructure
- Is future-proof for Revit 2025

**The model itself remembers what happened.**

