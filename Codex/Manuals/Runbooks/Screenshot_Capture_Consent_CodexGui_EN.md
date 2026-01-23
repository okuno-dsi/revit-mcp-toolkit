# Screenshot Capture + Consent Gate (Codex GUI)

This runbook explains how to capture screenshots via RevitMCP and **attach them to Codex** safely with an explicit consent step.

## What this does (safe by default)
- Captures screenshots **locally** using server-side tools (`capture.*`) + `CaptureAgent` (external process).
- Shows a **preview** and requires explicit approval before any image is attached to Codex (`codex --image ...`).
- High-risk captures (likely model/sheet canvas) are **unchecked by default** and require an extra confirmation.

## Prerequisites
- `RevitMCPServer` is running (the Capture tools are server-local).
- `CaptureAgent` is bundled under the server output folder (`capture-agent\\RevitMcp.CaptureAgent.exe`).
- `Codex GUI` is running and can detect the server port via `%LOCALAPPDATA%\\RevitMCP\\server_state.json`.

## Steps (Codex GUI)
1. Open Codex GUI.
2. Click `Capture` (top bar).
3. In the `Capture (Consent Gate)` window:
   - Choose `Target`:
     - `Revit dialogs (active_dialogs)` is recommended for errors/warnings.
     - `Revit main window` / `floating_windows` can include model views (high risk).
     - `Full screen` is high risk.
   - Click `Capture`.
4. Review the list and preview:
   - Select only the images you are comfortable sending.
   - Click `Open` to view in your default image viewer if needed.
5. Click `Approve Selected`.
   - If any `risk: high` images are selected, you must confirm again.
6. Run Codex normally (press `Enter` in Codex GUI).
   - The approved images are attached only to **the next run**.

## Notes
- Captures are stored under `%LOCALAPPDATA%\\RevitMCP\\captures\\` by default.
- A JSONL log is written to `%LOCALAPPDATA%\\RevitMCP\\logs\\capture.jsonl`.
- The server does **not** auto-send images to Codex; the consent gate is in Codex GUI.

## Troubleshooting
- Codex GUI crashes / no obvious error
  - Check `%LOCALAPPDATA%\\RevitMCP\\logs\\codexgui.log` (unhandled exceptions are written there).
  - If the log file becomes too large, Codex GUI rotates it at 10MB (`codexgui_yyyyMMdd_HHmmss.log`) in current builds.
- `ERR: CaptureAgent failed.` / `CAPTURE_AGENT_EXIT_NONZERO`
  - Ensure `server\\capture-agent\\RevitMcp.CaptureAgent.exe` **and** `RevitMcp.CaptureAgent.dll` exist under the deployed `RevitMCPServer` folder.
  - Older server builds sometimes preferred a stale `server\\RevitMcp.CaptureAgent.exe` stub in the server root; if its companion `.dll` is missing, capture fails. Current builds prefer `capture-agent\\`.
- `NO_REVIT_WINDOWS`
  - Revit is not running or no visible Revit window is detected. Bring Revit to foreground, or use `Target=Full screen` (screen).
