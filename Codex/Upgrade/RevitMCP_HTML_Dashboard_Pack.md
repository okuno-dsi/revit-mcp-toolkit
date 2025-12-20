
# RevitMCP HTML Dashboard — AI‑Agent Implementation Pack (Design + Samples)

**Version:** v0.1 (Prototype)  
**Scope:** A minimal, AI‑agent friendly dashboard that renders *project summaries and report tables* in the browser from RevitMCP JSON‑RPC endpoints, with clear contracts and drop‑in C# handlers for Revit add‑in (NET Framework 4.8 / C# 8) and a static HTML frontend (ASP.NET Core wwwroot).

---

## 1) Goals

- Show a **Top page** with:
  - **Project Info** (name, version, path, cloud flag, unit system, views, categories, phases)
  - **Worksharing** (is workshared, workset count), **Design Options**, **Warnings (count)**
  - **Levels**
  - **Rooms & Areas by Level**
  - **Elements by Category** and **Used Type Count**
  - A **link list of report titles** (separate HTML pages) below the cards
- Clicking a report title opens a dedicated **report page** that displays a tabular HTML view generated from `/rpc` calls.
- Provide **clear JSON‑RPC contracts**, **Revit handlers**, **server setup**, and **HTML prototypes** so an AI agent can implement and extend easily.

---

## 2) High‑Level Architecture

```
+------------------------------+         +-------------------------------+
|  Browser (HTML/JS)           |  POST   |  ASP.NET Core server          |
|  - index.html (Top)          | <-----> |  - /rpc (JSON-RPC bridge)     |
|  - report_*.html (Tables)    |         |  - wwwroot/ (static HTML)     |
+------------------------------+         +-------------------------------+
                                                 |
                                                 | (queue/poll or direct, as in your setup)
                                                 v
                                       +------------------------+
                                       | Revit Add-in           |
                                       | - IRevitCommandHandler |
                                       | - RevitMcpWorker       |
                                       +------------------------+
```

- The HTML pages are **static** and live under `wwwroot/`. They **POST /rpc** to retrieve data.
- Each `/rpc` **method** is implemented by a **Revit add‑in handler**, returning a **JSON object** with `{ ok, items?, ... }` so errors can be surfaced cleanly to users and agents.

---

## 3) JSON‑RPC Contracts

> AI agents should treat any `ok=false` response as a hard failure and surface the `msg` field.

### 3.1 `get_project_summary`
**Request**: `{"jsonrpc":"2.0","id":"...","method":"get_project_summary","params":{}}`  
**Response**:

```json
{
  "ok": true,
  "projectName": "Office A",
  "revitVersion": "Autodesk Revit 2024 (2024)",
  "docPath": "C:\\projects\\OfficeA.rvt",
  "isCloudModel": false,
  "unitSystem": "Metric",
  "levels": 12,
  "views": 248,
  "categories": 96,
  "phases": ["Existing","New Construction"],
  "isWorkshared": true,
  "worksets": 24,
  "warnings": 31,
  "designOptions": 2
}
```

### 3.2 `summarize_elements_by_category`
**Request**: `{"method":"summarize_elements_by_category","params":{"includeAnnotations":false}}`  
**Response**:

```json
{ "ok": true, "items": [ { "categoryName": "Walls", "count": 513 }, ... ], "total": 4390 }
```

### 3.3 `summarize_family_types_by_category` (optional)
**Request**: `{"method":"summarize_family_types_by_category","params":{}}`  
**Response**:

```json
{ "ok": true, "items": [ { "categoryName": "Walls", "typeCount": 28 }, ... ] }
```

### 3.4 `summarize_rooms_by_level`
**Request**: `{"method":"summarize_rooms_by_level","params":{}}`  
**Response**:

```json
{
  "ok": true,
  "items": [
    { "levelName": "L1", "rooms": 25, "totalAreaM2": 912.6 },
    { "levelName": "L2", "rooms": 22, "totalAreaM2": 845.1 }
  ],
  "totalRooms": 47,
  "totalAreaM2": 1757.7
}
```

### 3.5 `list_levels_simple`
**Request**: `{"method":"list_levels_simple","params":{}}`  
**Response**:

```json
{
  "ok": true,
  "items": [ { "id": 12345, "name": "L1", "elevation": 0.0 }, ... ]
}
```

### 3.6 `get_warnings_summary` (optional)
**Response shape (example)**:

```json
{ "ok": true, "items": [ { "kind": "Overlapping Walls", "count": 12 }, { "kind": "Room not enclosed", "count": 3 } ] }
```

---

## 4) Revit Add‑in — Minimal Handlers (C# 8, .NET Framework 4.8)

> All handlers implement `IRevitCommandHandler` and return POCO/anonymous objects serialized to JSON.  
> Put files under your existing folders (e.g. `Commands/DocumentOps`, `Commands/AnalysisOps`, `Commands/Room`, etc.).

### 4.1 `GetProjectSummaryCommand.cs`

```csharp
// RevitMCPAddin/Commands/DocumentOps/GetProjectSummaryCommand.cs
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.DocumentOps
{
    public class GetProjectSummaryCommand : IRevitCommandHandler
    {
        public string CommandName => "get_project_summary";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var app = uiapp.Application;
            var isWorkshared = doc.IsWorkshared;

            int worksets = 0;
            try {
                if (isWorkshared) worksets = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset).Count();
            } catch { }

            int nonTemplateViews = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Count(v => !v.IsTemplate);
            int categoriesCount = 0; try { categoriesCount = doc.Settings.Categories.Size; } catch { }
            int warnings = 0; try { warnings = doc.GetWarnings()?.Count ?? 0; } catch { }
            bool isCloud = false; try { isCloud = doc.IsModelInCloud; } catch { }
            string unitSystem = ""; try { unitSystem = doc.DisplayUnitSystem.ToString(); } catch { }

            int designOptions = 0;
            try { designOptions = new FilteredElementCollector(doc).OfClass(typeof(DesignOption)).ToElements().Count; } catch { }

            var phases = new List<string>();
            try { phases = doc.Phases?.Cast<Phase>().Select(p => p.Name).ToList() ?? new List<string>(); } catch { }

            return new {
                ok = true,
                projectName = doc.Title,
                revitVersion = $"{app.VersionName} ({app.VersionNumber})",
                docPath = doc.PathName ?? "",
                isCloudModel = isCloud,
                unitSystem = unitSystem,
                levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Count(),
                views = nonTemplateViews,
                categories = categoriesCount,
                phases = phases,
                isWorkshared = isWorkshared,
                worksets = worksets,
                warnings = warnings,
                designOptions = designOptions
            };
        }
    }
}
```

### 4.2 `SummarizeElementsByCategoryCommand.cs`

```csharp
// RevitMCPAddin/Commands/AnalysisOps/SummarizeElementsByCategoryCommand.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.AnalysisOps
{
    public class SummarizeElementsByCategoryCommand : IRevitCommandHandler
    {
        public string CommandName => "summarize_elements_by_category";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            bool includeAnnotations = false;
            try {
                includeAnnotations = (cmd.Params?.Value<bool?>("includeAnnotations") ?? false);
            } catch { }

            var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();

            foreach (var e in collector)
            {
                var c = e.Category;
                if (c == null) continue;
                if (!includeAnnotations && c.CategoryType == CategoryType.Annotation) continue;
                var key = c.Name ?? "(No category)";
                dict[key] = dict.TryGetValue(key, out var cur) ? (cur + 1) : 1;
            }

            var items = dict.Select(kv => new { categoryName = kv.Key, count = kv.Value })
                            .OrderByDescending(x => x.count).ToList();
            return new { ok = true, items = items, total = items.Sum(x => x.count) };
        }
    }
}
```

### 4.3 `SummarizeFamilyTypesByCategoryCommand.cs` (optional)

```csharp
// RevitMCPAddin/Commands/AnalysisOps/SummarizeFamilyTypesByCategoryCommand.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.AnalysisOps
{
    public class SummarizeFamilyTypesByCategoryCommand : IRevitCommandHandler
    {
        public string CommandName => "summarize_family_types_by_category";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            // Count *used* types = types with at least one instance placed.
            var result = new Dictionary<string, HashSet<ElementId>>(StringComparer.OrdinalIgnoreCase);

            var instances = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            foreach (var inst in instances)
            {
                var typeId = inst.GetTypeId();
                if (typeId == ElementId.InvalidElementId) continue;
                var cat = inst.Category;
                if (cat == null) continue;
                var key = cat.Name ?? "(No category)";
                if (!result.TryGetValue(key, out var set)) { set = new HashSet<ElementId>(); result[key] = set; }
                set.Add(typeId);
            }

            var items = result.Select(kv => new { categoryName = kv.Key, typeCount = kv.Value.Count })
                              .OrderByDescending(x => x.typeCount).ToList();
            return new { ok = true, items = items };
        }
    }
}
```

### 4.4 `SummarizeRoomsByLevelCommand.cs`

```csharp
// RevitMCPAddin/Commands/Room/SummarizeRoomsByLevelCommand.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Room
{
    public class SummarizeRoomsByLevelCommand : IRevitCommandHandler
    {
        public string CommandName => "summarize_rooms_by_level";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r != null && r.Area > 1e-6)
                .ToList();

            var byLevel = new Dictionary<string, (int count, double areaM2)>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rooms)
            {
                var name = r.Level != null ? r.Level.Name : "(No level)";
                var m2 = UnitUtils.ConvertFromInternalUnits(r.Area, UnitTypeId.SquareMeters);
                if (!byLevel.TryGetValue(name, out var cur)) cur = (0, 0.0);
                cur.count += 1; cur.areaM2 += m2;
                byLevel[name] = cur;
            }

            var items = byLevel.Select(kv => new {
                    levelName = kv.Key,
                    rooms = kv.Value.count,
                    totalAreaM2 = Math.Round(kv.Value.areaM2, 2)
                })
                .OrderBy(k => k.levelName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new {
                ok = true,
                items = items,
                totalRooms = rooms.Count,
                totalAreaM2 = Math.Round(items.Sum(i => i.totalAreaM2), 2)
            };
        }
    }
}
```

### 4.5 `ListLevelsSimpleCommand.cs`

```csharp
// RevitMCPAddin/Commands/DatumOps/ListLevelsSimpleCommand.cs
#nullable enable
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.DatumOps
{
    public class ListLevelsSimpleCommand : IRevitCommandHandler
    {
        public string CommandName => "list_levels_simple";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var items = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .Select(l => new {
                    id = l.Id.IntegerValue,
                    name = l.Name,
                    elevation = UnitUtils.ConvertFromInternalUnits(l.Elevation, UnitTypeId.Meters)
                })
                .OrderBy(x => x.elevation)
                .ToList();

            return new { ok = true, items = items };
        }
    }
}
```

### 4.6 Registration

```csharp
// RevitMcpWorker.cs — add handlers to the list
// using RevitMCPAddin.Commands.DocumentOps;
// using RevitMCPAddin.Commands.AnalysisOps;
// using RevitMCPAddin.Commands.Room;
// using RevitMCPAddin.Commands.DatumOps;

handlers.Add(new GetProjectSummaryCommand());
handlers.Add(new SummarizeElementsByCategoryCommand());
handlers.Add(new SummarizeFamilyTypesByCategoryCommand()); // optional
handlers.Add(new SummarizeRoomsByLevelCommand());
handlers.Add(new ListLevelsSimpleCommand());
```

---

## 5) ASP.NET Core Server (Static Files + /rpc)

Ensure **static file hosting** is enabled so `wwwroot/*.html` is served, and `/rpc` continues to route JSON‑RPC requests to your bridge.

```csharp
// Program.cs (.NET 8)
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
// ... your existing service setup

var app = builder.Build();

app.UseDefaultFiles(); // enables index.html fallback
app.UseStaticFiles();  // serves wwwroot

// /rpc endpoint already exists in your project (JSON-RPC bridge).
// app.MapPost("/rpc", ...);

app.MapGet("/health", () => Results.Ok(new { ok = true, ts = DateTimeOffset.Now }));
app.Run();
```

---

## 6) Prototype HTML Set

> Place these files into `wwwroot/`. They are **self‑contained** and call `/rpc` directly.  
> The **Top page** shows cards + a **link list of report titles**. Each report page renders a table view.

- `index.html` — Top page (cards + report links)
- `report_rooms.html` — Rooms by Level (table)
- `report_categories.html` — Elements & Types by Category (table)
- `report_levels.html` — Level List (table)
- `report_warnings.html` — Warnings (table; tries `get_warnings_summary`, falls back to total count)

**Download the prepared prototypes from this package or copy the snippets from your conversation.**

---

## 7) Implementation Notes & Conventions

- **Result Envelope**: Always return `{ ok: boolean, ... }`. On failure, add `msg` with a short human‑readable explanation:
  - `"ok": false, "msg": "No active document."`
- **Read‑Only Handlers**: The above handlers are *read only* (safe). No transactions are required.
- **Units**: Use Revit 2023+ `UnitTypeId` for conversions (e.g., areas to **SquareMeters**, elevations to **Meters**).
- **Type Counts**: We count **used** types by collecting instance `GetTypeId()` and building a distinct set per category.
- **Big Models**: For category histograms on very large projects, consider a **snapshot** cache (write JSON to disk and serve it) to keep UI instant.
- **Front‑End**: No frameworks, just `fetch("/rpc")`. Easily replaceable with your preferred UI later.
- **Accessibility**: Tables use semantic `<table>`; consider adding ARIA labels and CSV export if needed.

---

## 8) Testing Checklist

- `/rpc get_project_summary` returns values consistent with the opened model.
- `/rpc summarize_rooms_by_level` shows correct totals vs. Revit Room Schedule.
- `/rpc list_levels_simple` is sorted by elevation; spot‑check names & unit conversions.
- `/rpc summarize_elements_by_category` total equals the number of non‑type elements.
- If `summarize_family_types_by_category` is present, “Used Types” is non‑N/A on both Top and report pages.
- All pages load under `http://localhost:<port>/index.html` and links navigate to the reports.

---

## 9) Next Steps (Suggested)

1. **Add “Worksets by Element Count”**: histogram of elements per user workset.
2. **Add “Views by Type”**: counts of plans, sections, 3D, schedules, sheets.
3. **Export Buttons**: add CSV/Excel export (client‑side CSV or server‑side XLSX).
4. **Warning Details**: implement `get_warnings_summary` with subtype grouping and sample fix hints.
5. **Snapshot Mode**: implement `/snapshot/export` and pages that read the static JSON for performance.
6. **Auth / CORS**: if serving across hosts, add CORS or key‑based access. For local dev, keep simple.
7. **Mini Charts**: add inline bars (CSS‑only) or canvas for quick visual comparison.

---

## 10) File Tree (Minimal)

```
RevitMcpServer/
  wwwroot/
    index.html
    report_rooms.html
    report_categories.html
    report_levels.html
    report_warnings.html
  Program.cs
RevitMCPAddin/
  Commands/
    DocumentOps/GetProjectSummaryCommand.cs
    AnalysisOps/SummarizeElementsByCategoryCommand.cs
    AnalysisOps/SummarizeFamilyTypesByCategoryCommand.cs  (optional)
    Room/SummarizeRoomsByLevelCommand.cs
    DatumOps/ListLevelsSimpleCommand.cs
  RevitMcpWorker.cs   // register handlers
```

---

## 11) FAQ for AI Agents

**Q:** What if `summarize_family_types_by_category` is not implemented?  
**A:** The UI shows “N/A” in the Used Types column. You can add the handler later without changing the UI.

**Q:** How do I add a new report?  
**A:** Create `report_xyz.html`, call the relevant `/rpc` methods via `fetch`, and add a link on `index.html` under the “Reports” section.

**Q:** Is a transaction needed?  
**A:** No. All examples are read‑only. For write operations, wrap in `Transaction` and return `{ ok:false, msg }` on failure.

**Q:** Can this run with multiple Revit sessions?  
**A:** Yes, as long as your /rpc bridge queues and routes requests correctly to the active session/worker. Keep responses small or paginated.

---

**End of document.**

