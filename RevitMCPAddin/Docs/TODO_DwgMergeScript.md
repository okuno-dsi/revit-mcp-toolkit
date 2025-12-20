# DWG Merge Script – ToDo

Goals
- Make generation robust, discoverable from RevitMCP, and easy to run via AutoCadMCP or external runner.

Backlog
- Execution Integration
  - [ ] Add AutoCadMCP RPC: run_accore_script { accorePath, locale, seedDwg?, scriptPath, timeoutMs? }.
  - [ ] RevitMCP convenience: optional follow‑up call to AutoCadMCP when `execute=true` is passed.
  - [ ] Stream/log tailing to user (console capture or file pointer to accore log and output DWG).

- UI/UX (Optional, after script‑only)
  - [ ] Minimal WPF dialog to gather: inputDir, pattern, bindType, mergeMode(Map CSV), outputDwg, version.
  - [ ] Persist last used values (per‑user, %LocalAppData%).
  - [ ] Validate paths and show matched DWG count before generate.

- Merge Behavior
  - [ ] MergeMode=Map: validate CSV rows, show preview of target layers.
  - [ ] Optional layer renaming rules (prefix/suffix) after bind.
  - [ ] Optional exclude lists (e.g., `GRID`, `CENTER`, `TEXT`).

- Reliability
  - [ ] Guard against empty selection (no DWG matched) with clear error.
  - [ ] Auto‑create output folder if missing, with permission checks.
  - [ ] Toggle `SECURELOAD` and `TRUSTEDPATHS` templates safely.
  - [ ] Optional retries for `PURGE`/`AUDIT` heavy drawings.

- Config/DevEx
  - [ ] Job JSON input → map to command params (load from a `.json` and `layermap.csv`).
  - [ ] Sample jobs under `Work/AutoCadOut/Jobs` – keep in sync with the command docs.
  - [ ] Add `README_AcCoreJobs.txt` cross‑link in RevitMCP docs.

- Testing
  - [ ] Golden‑file tests for generated `.scr` (pattern → expected lines).
  - [ ] End‑to‑end smoke: generate + run on tiny seed.dwg, assert DWG exists.
  - [ ] Map mode test with 2–3 patterns.

- Diagnostics
  - [ ] Return `matched` DWG names as an array when `verbose=true`.
  - [ ] Optionally emit `accore` redirection to a known log file path.
  - [ ] Enrich errors with actionable hints (e.g., invalid SAVEAS path).

- Build/Platform
  - [ ] Ensure C# 8.0+ (nullable) is enabled in csproj to match codebase.
  - [ ] Confirm Revit 2023–2025 references; multi‑target if needed.

- Security/Paths
  - [ ] Sanitize/normalize paths strictly; avoid shell injection exposure.
  - [ ] Guard `TRUSTEDPATHS` inputs; optionally whitelist.

- Documentation
  - [ ] Link this command in existing `Manuals/` quickstarts.
  - [ ] Provide Japanese/English side‑by‑side examples.

Nice‑to‑have
- [ ] Generator: allow templated header/tail injection for enterprise standards.
- [ ] Detect and reuse an existing seed DWG (first matched) when none specified.
- [ ] Optional DXF export pipeline (SAVEAS DXF 2018).

