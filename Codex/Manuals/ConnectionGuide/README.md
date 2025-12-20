Commands Index (English, AI‑oriented)

Purpose
- Single place to browse all MCP command names, grouped by category with importance tags, and a machine‑readable index for tooling.

Files
- Commands_Index.all.en.md — Human‑friendly, all commands by category with (high/normal/low) and read/write hint.
- commands_index.json — Machine‑readable map: { method: { category, importance, kind } }.
- generate_commands_index.py — Generator script (reads names from the demo data folder).

Update Steps (when commands are added/removed)
1. Refresh the live command names (namesOnly):
   - `python Manuals/Scripts/send_revit_command_durable.py --port <PORT> --command list_commands --params '{"namesOnly":true}' --output-file list_commands_names.json`
   - Alternatively place an array of names in available_commands.json`.
2. Generate the index:
   - `python Commands_Index/generate_commands_index.py`
3. Review the diffs (importance/category heuristics) and commit.

Notes
- Importance and category are heuristic; adjust the `HIGH` set and keyword buckets in the generator as needed.
- For exact parameters/behavior, prefer the live environment and per‑command docs.


