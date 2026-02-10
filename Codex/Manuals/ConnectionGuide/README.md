Commands Index (English, AI‑oriented)

Purpose
- Single place to browse all MCP command names, grouped by category with importance tags, and a machine‑readable index for tooling.
- Source of truth is the live server: `GET /debug/capabilities` (or `docs/capabilities.jsonl`).

Files
- Full manuals (human-friendly, kept current): `Manuals/FullManual/README.md` and `Manuals/FullManual_ja/README.md`
- Commands_Index.all.en.md — Human‑friendly, all commands by category with (high/normal/low) and read/write hint.
- commands_index.json — Machine‑readable map: { method: { category, importance, kind } }.

Update Steps (when commands are added/removed)
0. Prefer live capabilities for the current build:
   - `GET http://127.0.0.1:<PORT>/debug/capabilities`
   - Or `list_commands { namesOnly:true }` (canonical only)
1. (Optional / legacy) If you still maintain `Manuals/Commands/commands_index.json`:
   - Refresh the live command names (namesOnly):
     - `python Scripts/Reference/send_revit_command_durable.py --port <PORT> --command list_commands --params '{"namesOnly":true}' --output-file list_commands_names.json`

Notes
- Importance and category are heuristic; adjust the `HIGH` set and keyword buckets in the generator as needed.
- For exact parameters/behavior, prefer the live environment and per‑command docs.





