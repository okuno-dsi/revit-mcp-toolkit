# IfcCli / IfcCore Manual (EN)

## 1. Overview

`IfcCli` is a lightweight command‑line tool for inspecting and validating IFC files.
It is built on a small core library called `IfcCore` that provides:

- A minimal in‑memory IFC model (`IfcModel`, `IfcEntity`).
- Property statistics and profile generation for data quality checks.
- Profile‑based validation against IFC property requirements.
- Simple level (storey) awareness using `IFCBUILDINGSTOREY` and placements.
- STEP extended Unicode decoding (for Japanese and other non‑ASCII names).

Target use cases include:

- Analysing IFC exports from Revit or other BIM tools to see which properties are filled.
- Defining a “profile” (set of required properties) from good sample files.
- Checking new IFC files against a profile to find missing or low‑fill properties.
- Listing elements by building storey (e.g., “all beams on 3FL”, “all rooms on 2FL”).

The tool is intentionally read‑only and does **not** modify IFC files.

---

## 2. Project Structure

Root folder (relative to the Codex repo):

- `IfcCli\`
  - `IfcCli.csproj` – CLI executable project (target: `net6.0`).
  - `Program.cs` – command‑line entry point and argument parsing.
  - `IfcCore\`
    - `IfcCore.csproj` – core library (also `net6.0`).
    - `Models.cs` – data models (entities, stats, profiles, check results).
    - `Services.cs` – analyzer, profile generator, checker interfaces and implementations.
    - `IfcLoader.cs` – text STEP parser with storey and Unicode handling.
    - `Logging.cs` – simple file logger.
  - `Docs\`
    - `IfcCli_Manual_en.md` – this file.
    - `IfcCli_Manual_ja.md` – Japanese manual.

Binary path after build:

- `IfcCli\bin\Release\net6.0\IfcCli.exe`

The tool is built as a normal .NET 6 console app and can be run directly with `dotnet` or as an EXE.

---

## 3. Building and Running

### 3.1 Build

From the `Codex\IfcCli` directory:

```powershell
cd C:\Users\okuno\Documents\VS2022\Ver602\Codex\IfcCli
dotnet build -c Release
```

Notes:

- The SDK may warn that `.NET 6` is EOL; this is expected but not fatal.
- A successful build creates `IfcCli.exe` under `bin\Release\net6.0`.

### 3.2 Running

From the release output directory:

```powershell
cd C:\Users\okuno\Documents\VS2022\Ver602\Codex\IfcCli\bin\Release\net6.0
.\IfcCli.exe --help
```

The help output lists supported commands:

- `analyze-sample`
- `check-ifc`
- `stats`
- `list-by-storey`
- `dump-spaces`

Each command writes a dated log file in the current directory:

- `IfcProfileAnalyzer_yyyyMMdd_HHmmss.log`

This log is useful for tracing errors and understanding what the tool did.

---

## 4. Core Concepts (IfcCore)

### 4.1 IfcModel and IfcEntity

`IfcModel` is a simple container for entities:

- `SourcePath` – full path of the IFC file.
- `EntitiesById` – dictionary keyed by numeric IFC ID (e.g., `#474 -> IfcEntity`).
- `EntitiesByType` – dictionary keyed by IFC type name (e.g., `"IFCSPACE"`, `"IFCBEAM"`).

`IfcEntity` represents a single IFC instance:

- `Id` – numeric ID (e.g., `474` for `#474`).
- `IfcType` – type name (e.g., `"IFCSPACE"`, `"IFCBEAM"`).
- `GlobalId` – IFC GUID (first argument).
- `Name` – decoded IFC name (third argument, STEP Unicode decoded).
- `StoreyId` – optional `IFCBUILDINGSTOREY` numeric ID (if known).
- `StoreyName` – storey label (e.g., `"1FL"`, `"2FL"`) if assigned.
- `Properties` – dictionary of `PropertyKey -> bool` indicating presence of a value.

`PropertyKey`:

- `Pset` – property set name (e.g., `Pset_SpaceCommon`).
- `Prop` – property name (e.g., `FloorCovering`).

The value in `Properties` is `true` if the property is present and non‑empty, `false` or absent otherwise.
The tool does not store actual numeric/strings values, only presence flags for analysis.

### 4.2 AnalysisResult and ProfileDefinition

`AnalysisResult` aggregates statistics for one or more IFC files:

- `SourceFiles` – list of IFC paths analysed.
- `Entities` – dictionary keyed by IFC type name.

`EntityStats`:

- `InstanceCount` – total count of entities of that type.
- `Properties` – dictionary of `PropertyKey -> PropertyStats`.

`PropertyStats`:

- `EntityCount` – number of entities where the property exists.
- `ValueCount` – number of entities where the property has a non‑empty value.
- `FillRate` – `ValueCount / EntityCount`.

`ProfileDefinition` is a compact summary of “required properties”:

- `ProfileName`, `ProfileVersion`, `CreatedAt`.
- `SourceFiles` – IFC file list used to generate this profile.
- `EntityRules` – `IfcType -> EntityRule`.

`EntityRule`:

- `RequiredProperties` – list of `RequiredPropertyRule`
  - `Pset`, `Name`, `MinFillRate` – the minimum acceptable fill rate.

### 4.3 CheckResult

`CheckResult` captures the outcome of `check-ifc`:

- `Ok` – `true` if no violations, `false` otherwise.
- `ProfileName` – name from the profile JSON.
- `TargetFile` – IFC being checked.
- `Summary` – error and warning counts.
- `Items` – detailed issues (`CheckItem`).

`CheckItem`:

- `Severity` – `"error"` or `"warning"`.
- `EntityName` – IFC type (e.g., `IFCSPACE`).
- `IfcGuid` – entity GUID.
- `Pset`, `Property` – property that failed the rule.
- `Message` – human‑readable explanation.

### 4.4 Storey (Level) Handling

Storey information is derived in two steps:

1. **Direct containment (preferred)**  
   `IFCRELCONTAINEDINSPATIALSTRUCTURE` records link elements to `IFCBUILDINGSTOREY`:

   - The loader parses all `IFCBUILDINGSTOREY` entities:
     - `Id` – numeric ID (e.g., `#114`).
     - Name – decoded (e.g., `"2FL"`).
     - Elevation – Z value (in IFC length units, usually mm).
   - For each `IFCRELCONTAINEDINSPATIALSTRUCTURE` where the `RelatingStructure` is a building storey,
     entities in `RelatedElements` get `StoreyId` and `StoreyName` set.

2. **Fallback for `IFCSPACE`**  
   Some spaces are not contained via a storey relation. For these:

   - The loader builds Z coordinates for:
     - `IFCCARTESIANPOINT` (list of coordinates).
     - `IFCAXIS2PLACEMENT3D` (location’s Z from the referenced point).
     - `IFCLOCALPLACEMENT` (recursive: parent placement Z + local Axis2Placement3D Z).
   - For each `IFCSPACE` without storey:
     - It resolves `ObjectPlacement`, computes its Z position.
     - It finds the nearest storey by absolute elevation difference.
     - Sets `StoreyId` and `StoreyName` accordingly.

This is sufficient for questions like “which spaces are on 2FL?” for typical Revit‑exported IFCs.

### 4.5 STEP Unicode Decoding

IFC text can be encoded using STEP extended encoding, for example:

- `'\X2\4E8B52D95BA4\X0\201'` → `"事務室201"`  
- `'\X2\97627A4D\X0\:8261599'` → `"面積:8261599"`

The loader’s `Unwrap` method:

- Removes surrounding single quotes.
- Detects the `\X2\...\X0\` pattern.
- Converts each 4‑digit hex chunk into a Unicode code point.
- Returns a pure Unicode `string` (UTF‑16) to the rest of the code.

This decoding is applied to:

- Building storey names.
- Entity names (e.g., `IFCSPACE.Name`, `LongName` where applicable).
- Property set names and property names.

The CLI commands always see decoded strings; no additional decoding is required.

---

## 5. Commands

All commands share the same binary (`IfcCli.exe`). The first argument is the command name.

### 5.1 Common Behavior

- The first argument is the command (`analyze-sample`, `check-ifc`, etc.).
- Remaining arguments are options (e.g., `--ifc`, `--out`, `--storey`).
- Each run creates a log file `IfcProfileAnalyzer_yyyyMMdd_HHmmss.log` in the current directory.

Example generic pattern:

```powershell
.\IfcCli.exe <command> [options]
```

### 5.2 `analyze-sample`

Generate a profile definition from one or more sample IFC files.

**Usage**

```powershell
.\IfcCli.exe analyze-sample --input sample1.ifc [sample2.ifc ...] --out profile.json --min-fill-rate 0.9
```

**Options**

- `--input` `<path ...>`  
  One or more IFC files. The parser continues until it finds another option starting with `--`.

- `--out` `<file>`  
  Output path for the profile JSON (default: `profile.json` in the current directory).

- `--min-fill-rate` `<double>`  
  Minimum fill rate threshold used when selecting required properties (default: `0.9`).

**Behavior**

1. Loads all specified IFC files using `IfcLoader`.
2. Aggregates property statistics with `Analyzer`.
3. Generates a `ProfileDefinition` with `ProfileGen`, selecting properties whose fill rate
   meets or exceeds `min-fill-rate`.
4. Writes the profile as pretty‑printed JSON.

This profile is later consumed by `check-ifc`.

### 5.3 `check-ifc`

Check an IFC file against a previously generated profile.

**Usage**

```powershell
.\IfcCli.exe check-ifc --ifc model.ifc --profile profile.json --out check.json
```

**Options**

- `--ifc` `<file>` – IFC file to check (required).
- `--profile` `<file>` – Profile JSON from `analyze-sample` (required).
- `--out` `<file>` – Output JSON file for `CheckResult` (default: `check.json`).

**Behavior**

1. Loads the profile JSON into `ProfileDefinition`.
2. Loads the target IFC file with `IfcLoader`.
3. Uses `ProfileCheck` to compare actual property fill rates against required thresholds.
4. Writes a `CheckResult` JSON including:
   - Overall `Ok` flag.
   - Summary counts.
   - Detailed `Items` describing missing/under‑filled properties.

Return code is always `0` for `check-ifc`; callers should inspect the JSON for severity.

### 5.4 `stats`

Produce a complete property statistics report for one IFC file.

**Usage**

```powershell
.\IfcCli.exe stats --ifc model.ifc --out stats.json
```

**Options**

- `--ifc` `<file>` – IFC file to analyse (required).
- `--out` `<file>` – Output JSON path (default: `stats.json`).

**Behavior**

1. Runs the same analysis as `analyze-sample`, but for a single IFC file.
2. Outputs `AnalysisResult` as JSON, including:
   - For each `IfcType`: instance count.
   - For each property: entity count, value count, fill rate.

Use this when you want a raw, unfiltered view of property coverage in one IFC file.

### 5.5 `list-by-storey`

List entities on a specific building storey, optionally filtered by IFC type.

**Usage**

```powershell
.\IfcCli.exe list-by-storey --ifc model.ifc --storey 3FL [--type IFCBEAM]
```

**Options**

- `--ifc` `<file>` – IFC file to inspect (required).
- `--storey` `<name>` (alias: `--level`) – storey name, e.g. `1FL`, `2FL`, `3FL` (required).
- `--type` `<IFC type>` – optional filter, e.g., `IFCBEAM`, `IFCSPACE`.

**Behavior**

1. Loads the IFC file and constructs `IfcModel` with storey assignments.
2. Filters entities with `ent.StoreyName == storey` (case‑insensitive).
3. If `--type` is given, further filters by `ent.IfcType`.
4. Sorts matches by numeric ID and prints each as:

   ```text
   Elements on storey '2FL' of type IFCSPACE: count=...
     #474  type=IFCSPACE  Name=33  GlobalId=...
   ```

**Common uses**

- “List all beams on 3rd floor”  
  `--storey 3FL --type IFCBEAM`

- “List all rooms/spaces on 2nd floor”  
  `--storey 2FL --type IFCSPACE`

Storey names come from decoded `IFCBUILDINGSTOREY.Name`, so Japanese names would also work.

### 5.6 `dump-spaces` (diagnostic)

Diagnostic command to inspect all `IFCSPACE` entities and their storey assignments.

**Usage**

```powershell
.\IfcCli.exe dump-spaces --ifc model.ifc
```

**Behavior**

1. Loads the IFC file.
2. Counts all entities of type `IFCSPACE`.
3. Prints each space as:

   ```text
   IFCSPACE count=120
     #474  Name=33  Storey=2FL
     #506  Name=34  Storey=2FL
     ...
   ```

This is primarily intended for debugging storey inference and verifying that names are decoded correctly.

### 5.7 `export-ids`

Export a `ProfileDefinition` JSON (from `analyze-sample`) to a buildingSMART IDS XML file.

**Usage**

```powershell
.\IfcCli.exe export-ids --profile profile.json --out review_requirements.ids.xml [--include-comments]
```

**Options**

- `--profile` `<file>` – Input profile JSON generated by `analyze-sample` (required).
- `--out` `<file>` – Output IDS XML file (required).
- `--include-comments` – When present, adds XML comments with profile details and fill rates.

**Behavior**

1. Loads the profile JSON into `ProfileDefinition`.
2. For each entity rule in `profile.EntityRules`:
   - Creates a `<specification>` with `name="<Entity>_Requirements"`.
   - Adds `<applicability><entity>EntityName</entity></applicability>`.
   - Adds `<requirements>` with one `<property>` per required property:

     ```xml
     <property>
       <pset>Pset_SpaceCommon</pset>
       <name>GrossArea</name>
       <occurrence>required</occurrence>
     </property>
     ```

3. Writes an IDS XML document of the form:

   ```xml
   <ids>
     <specification name="IfcSpace_Requirements">
       <applicability>
         <entity>IfcSpace</entity>
       </applicability>
       <requirements>
         <!-- property entries -->
       </requirements>
     </specification>
     <!-- other specifications per entity -->
   </ids>
   ```

Error handling:

- Invalid or unreadable profile JSON → error message, exit code `1`.
- Output file write failure (e.g., permission issues) → error message, exit code `2`.


---

## 6. Typical Workflows

### 6.1 Define a Property Profile from Good IFC Samples

1. Export one or more high‑quality IFC files from Revit (or other BIM tools).
2. Run:

   ```powershell
   .\IfcCli.exe analyze-sample `
     --input good1.ifc good2.ifc `
     --out profile.json `
     --min-fill-rate 0.9
   ```

3. Review `profile.json` to confirm required properties.

### 6.2 Check New IFC Files Against the Profile

1. Generate a profile as above.
2. For each new IFC file, run:

   ```powershell
   .\IfcCli.exe check-ifc --ifc new.ifc --profile profile.json --out new_check.json
   ```

3. Inspect `new_check.json` to see:
   - Which entity types and properties fail.
   - Whether missing data is acceptable or needs correction upstream in BIM authoring.

### 6.3 Answer Level‑Based Questions

Examples:

- “Which beams are on 3FL?”

  ```powershell
  .\IfcCli.exe list-by-storey `
    --ifc Revit_BIM申請サンプルモデル_01.ifc `
    --storey 3FL `
    --type IFCBEAM
  ```

- “Which rooms are on 2FL?”

  ```powershell
  .\IfcCli.exe list-by-storey `
    --ifc Revit_BIM申請サンプルモデル_01.ifc `
    --storey 2FL `
    --type IFCSPACE
  ```

If storey assignments fail for some spaces, run `dump-spaces` to investigate:

```powershell
.\IfcCli.exe dump-spaces --ifc Revit_BIM申請サンプルモデル_01.ifc
```

---

## 7. Input Assumptions and Limitations

### 7.1 IFC Version and Scope

- The loader expects text STEP files (IFC2x3 or IFC4).  
- It only fully understands a small set of entities:
  - `IFCPROPERTYSET`, `IFCPROPERTYSINGLEVALUE`.
  - `IFCRELDEFINESBYPROPERTIES`.
  - `IFCBUILDINGSTOREY`.
  - `IFCRELCONTAINEDINSPATIALSTRUCTURE`.
  - `IFCCARTESIANPOINT`, `IFCAXIS2PLACEMENT3D`, `IFCLOCALPLACEMENT` (for Z and storey inference).
- Geometry (solids, faces, etc.) is not interpreted; only placement Z is used.

For other entities, the loader still creates `IfcEntity` instances, but does not attach geometry.

### 7.2 Property Values vs Presence

- The tool tracks whether a property has a value (`hasValue`), not the actual value.
- Complex properties (e.g., enumerations, measures) are currently treated as “hasValue” vs “missing/empty”.

If you need to inspect actual numeric or string values, the analyzer would need to be extended.

### 7.3 Storey Inference Boundaries

- Fallback storey assignment chooses the closest storey by vertical distance.
- There is currently no maximum distance threshold. Extremely unusual placements may still be assigned to the nearest storey even if they are far away.
- The logic is tuned primarily for Revit‑style models where spaces are laid out per level.

If desired, a tolerance can be added later (for example, “do not assign if |Z - Elevation| > N”).  

### 7.4 Performance

- The parser reads files line‑by‑line and keeps the full raw entity map in memory.
- For typical architectural models, performance should be acceptable.
- Very large IFC files may require more memory; the tool currently does not stream entities.

---

## 8. Extensibility Notes

The design aims to be straightforward to extend:

- To add a new CLI command:
  - Implement a `RunXxx` method in `Program.cs`.
  - Add a case in the main command switch.

- To analyze additional relationships or entities:
  - Extend `IfcLoader` to recognise more types and relationships.
  - Add new fields to `IfcEntity` as needed.

- To output more detailed data (e.g., JSON listing of rooms by storey):
  - Use `IfcModel.EntitiesByType` / `EntitiesById` and `System.Text.Json`.

Because Unicode decoding is centralised in `Unwrap`, all new code that reads STRING arguments will automatically benefit from correct Japanese and other extended characters.  
