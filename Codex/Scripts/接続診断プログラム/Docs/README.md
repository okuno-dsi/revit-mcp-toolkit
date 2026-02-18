# RevitMCP Diagnostic Program (Port 5210)

## Folder Layout
- `Run_RevitMCP_Diagnostics_5210_Full.cmd`: double-click launcher
- `Scripts\diagnose_revitmcp_5210.ps1`: diagnostic core script
- `Output\`: generated reports (`diagnostics_*.json`, `diagnostics_*.md`)
- `Docs\`: documentation

## Usage
1. Double-click `Run_RevitMCP_Diagnostics_5210_Full.cmd`.
2. Wait until completion.
3. Send files in `Output\` for analysis.

## Notes
- Fixed port: `5210`
- Full diagnostics mode
- Safe to run repeatedly; each run writes timestamped files.
