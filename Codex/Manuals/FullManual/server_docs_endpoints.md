# Server docs endpoints (OpenRPC/OpenAPI/Markdown)

The server can expose auto‑generated docs based on the add‑in manifest.

## Manifest registration

The Revit add‑in posts its command manifest to the server on startup (best‑effort):
- `POST /manifest/register`

You can inspect what the server currently knows:
- `GET /docs/manifest.json`

## Generated docs

- `GET /docs/openrpc.json` — OpenRPC (JSON‑RPC method list)
- `GET /docs/openapi.json` — OpenAPI (virtual `/rpc/{method}` paths)
- `GET /docs/commands.md` — human‑readable markdown list

Notes:
- The docs reflect the most recent manifest received (or cached on disk).
- If the manifest is empty, start Revit and ensure the add‑in connects to the same server port.

