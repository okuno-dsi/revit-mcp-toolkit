# Update Notes (2025-12-23)

## Documentation
- Updated command index files for English (`Codex/Manuals/Commands/Commands_Index.all.en.md`, `Codex/Manuals/Commands/commands_index.json`).
- Added/updated FullManual and FullManual_ja command docs, including:
  - agent bootstrap, describe command, get context, revit status
  - create focus 3D view from selection, create view plan
  - dry run and confirm token, server docs endpoints
  - material asset and thermal properties commands
  - set wall top to overhead, sheet inspect, view diagnose visibility
- Updated scripts and script documentation:
  - create focus 3D from selection
  - set wall top to overhead
  - terminology routing test script

## RevitMCPAddin
- Added/updated commands:
  - material asset helpers
  - set wall top to overhead
  - agent bootstrap / describe / search commands
  - get context
  - view commands (focus 3D from selection, create view plan, diagnose visibility, sheet inspect)
- Added core services for confirm tokens, parameter teaching, and term mapping.
- Updated command routing, manifest exporter, project file, and worker.
