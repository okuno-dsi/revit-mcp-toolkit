# RevitMCP Server

This project provides an open integration layer (server + Revit add-in) to **safely operate and orchestrate Autodesk Revit®** from external tools (including AI agents, CLIs, and automation systems).

There may be other projects exploring similar ideas worldwide; however, as far as we know, this implementation is **explicitly designed to be usable in Japanese environments**, enabling workflows aligned with Japan-specific standards and regulations (e.g., local building codes and organizational guidelines).

---

## Important Notices (Disclaimer & Requirements)

- This project is released under **Apache License 2.0** (see `LICENSE`).
- This project is **not affiliated with, endorsed by, or sponsored by Autodesk, Inc.**
- This repository **does not ship or redistribute** Autodesk materials (Revit binaries, Revit SDK, DLL/CHM files, or Autodesk sample code).
  A **valid Autodesk Revit® license** is required to use the add-in.
- The software is provided **“AS IS”** without warranties. See the warranty disclaimer and limitation of liability in `LICENSE`.

---

## Getting Started
- Start with `Codex/START_HERE.md` (quick path: connection check → basic operations).

---

## Runtime Prerequisites
- **.NET 8 Runtime** (e.g. `RevitMCPServer` / `ExcelMCP` / `RhinoMcpServer` / `AutoCadMcpServer`)
- **.NET 6 Runtime** (`Codex/CodexGui`)
- **.NET Framework 4.8** (`RevitMCPAddin` / `RhinoMcpPlugin`)

---

## Related MCP Components
This repository focuses on Revit MCP. Under the same concept, there may be related components (possibly in separate repositories/distributions):

- **Excel MCP**
- **Rhino MCP**
- **AutoCAD MCP**

Please refer to the documentation of each component for its distribution terms, dependencies, and usage.

---

## What it can do (Examples)

We turned as much as possible of what is achievable via the Revit API into commands.  
Have an AI agent read the manuals and ask it what you want to do.

---

## Architecture Overview

- **External server process**: entry point for MCP / JSON-RPC requests from clients (AI/CLI/other tools).
- **Revit Add-in (.NET Framework 4.8)**: executes actual operations via the official Revit API.
- **Transport**: local IPC/HTTP/etc. (depends on implementation).
- **Extensible**: add features in command units; tailor to templates, parameter schemas, and organizational rules.

---

## Security & Operational Guidance

- This project provides powerful automation capabilities. Operate it under the **principle of least privilege**.
- In shared environments, consider authentication, access control, and audit logging according to your policies.
- When used with generative AI, plan for potential mis-operations:
  - dry-run / confirmation flows
  - change diff previews
  - extra confirmation for critical operations

---

## Legal / IP / Confusion Avoidance (Risk Mitigation)

### Independent Implementation
This project is independently implemented using the official **Autodesk Revit API**.  
It does **not** copy, decompile, reverse-engineer, or incorporate proprietary code from other products/services.

### Trademarks
Autodesk® and Revit® are trademarks of Autodesk, Inc.  
Please avoid names, logos, or descriptions that could cause confusion with third-party products.

### Patents (Apache 2.0)
Apache License 2.0 includes an explicit patent license from contributors for their contributions.  
This does not eliminate potential third‑party patent risks; consult your legal team if needed.

---

## Distribution Policy (Binaries)
This repository is published primarily as **source code**.  
If your organization builds and distributes binaries internally, ensure compliance with Autodesk licensing terms and your development tool licensing (e.g., Visual Studio).

---

## Credits
- Created by **Tetsuya Okuno** and **GPT series** (2025) — 99.9% vibe coding

---

## License
- **Apache License 2.0** (see `LICENSE`)
