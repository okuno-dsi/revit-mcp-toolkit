# Client‑Side Caching and Server/Add‑in Change Policy (EN)

This note clarifies that no changes are required to the Revit add‑in or the RevitMCPServer, and documents a safe, low‑overhead workflow to cache frequently used project/document data on the client side.

---

## Summary

- No server/add‑in change required.
- Use client‑side caching to avoid repeated queries against large Revit models.
- Reuse existing HTTP endpoints via Proxy → Playbook → RevitMCPServer chain.
- Keep Playbook forwarding to a Revit port (not the proxy) to avoid routing loops.

---

## Topology & Ports

- Playbook Server: `http://127.0.0.1:5209`
- Proxy: `http://127.0.0.1:5221` (chosen to avoid conflicts with Revit 5211/5212)
- RevitMCPServer (inside Revit): `5210` (first instance), `5211/5212` for additional instances
- Dynamic targeting: `POST /t/{revitPort}/rpc` (and `/enqueue`, `/job/{id}`)

---

## Caching Workflow (One‑Shot)

1) Create/refresh caches

````powershell
python "%USERPROFILE%\\Documents\\Revit_MCP\\Codex\Manuals\Scripts\cache_revit_info.py" `
  --proxy http://127.0.0.1:5221 `
  --revit-port 5211 `
  --out-dir "%USERPROFILE%\\Documents\\Revit_MCP\\Codex\Manuals\Logs" `
  --ttl-sec 0  # set >0 (e.g. 600) to reuse within TTL
````

- Saves:
  - `Codex/Projects/<ProjectName>_<Port>/Logs/project_info_<port>.json`
  - `Codex/Projects/<ProjectName>_<Port>/Logs/open_documents_<port>.json`

2) Read cached data (concise summary)

````powershell
pwsh -File "%USERPROFILE%\\Documents\\Revit_MCP\\Codex\Manuals\Scripts\get_project_and_documents_cached.ps1" `
  -Port 5211 `
  -Proxy http://127.0.0.1:5221 `
  -OutDir "%USERPROFILE%\\Documents\\Revit_MCP\\Codex\Manuals\Logs"
````

- Options:
  - `-Refresh` to force re‑query then save JSON
  - `-TtlSeconds 600` to reuse cache up to 10 minutes
  - `-Full` to output the full cached JSON

3) Consume JSON directly (applications/tools)

- JSON structure: `{ "ts", "port", "method", "result": { ... } }`
- Read from `project_info_<port>.json` and `open_documents_<port>.json` in UTF‑8.

---

## Why No Server/Add‑in Changes?

- The caching layer uses existing endpoints (`/enqueue`, `/job/{id}`) provided by RevitMCPServer.
- Playbook and Proxy forward requests as they already do; no new behavior is required server‑side.
- All logic (TTL, refresh, file I/O) lives in client scripts:
  - `Codex/Scripts/Reference/cache_revit_info.py`
  - `Codex/Scripts/Reference/get_project_and_documents_cached.ps1`

---

## Best Practices

- Prefer per‑request targeting for multi‑Revit environments: `POST /t/5211/rpc`.
- Keep Playbook `--forward` pointing at a Revit port (e.g., 5210). Do not forward Playbook to the Proxy.
- Separate caches by Revit port to avoid mixing data across instances.
- Check caches into source control only if they are small, anonymized, and stable; otherwise keep local under `Codex/Projects/<ProjectName>_<Port>/Logs`.

---

## Troubleshooting

- AddressInUse / WinError 10013: port conflict. Keep Proxy on 5221+, Playbook on 5209, Revit on 5210/5211/5212.
- Empty console: when backgrounded, output is in logs/other windows; run in foreground for live logs.
- Garbled characters in console: JSON files are UTF‑8; open in an editor or ensure console UTF‑8 (e.g., `chcp 65001`).
- Slow first call: normal on large models; once cached, downstream tools should read the saved JSON.

---

## Related Files

- Playbook: `McpPlaybookServer/src/McpPlaybookServer`
- Proxy: `ChatRevit/proxy_mcp_logger.py`
- Scripts (caching):
  - `Codex/Scripts/Reference/cache_revit_info.py`
  - `Codex/Scripts/Reference/get_project_and_documents_cached.ps1`
- Quickstart: `Codex/Manuals/ConnectionGuide/Revit_Connection_OneShot_Quickstart_EN.md` (see “Caching” section)









