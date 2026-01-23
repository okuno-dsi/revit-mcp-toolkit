# RevitMCP Collaborative Chat (Quick Guide)

This feature adds a **human-first collaborative chat** (append-only JSONL as the system of record) and a safe manual handoff into Codex GUI (`Copy→Codex`).

## Where data is stored
- Root: `<CentralModelFolder>\_RevitMCP\projects\<docKey>\`
- Chat events: `_RevitMCP\projects\<docKey>\chat\writers\*.jsonl` (per-writer append-only logs)
- Cloud models (ACC/BIM 360) are not supported for chat storage. Chat is disabled when the active document is a cloud model.
  - `docKey` is a stable per-project identifier (same as ViewWorkspace/Ledger ProjectToken) to avoid collisions when multiple `.rvt` live in the same folder.
  - Fallback when `docKey` is missing: `projects\path-<hash>\...` (temporary).

## Server-local RPC methods
These do **not** enqueue Revit jobs (no DurableQueue). They are handled directly by `RevitMCPServer`.

- `chat.post` (Write)
- `chat.list` (Read)
- `chat.inbox.list` (Read; currently: messages that mention `@userId`)

Important: the server must know the project root. Provide `docPathHint` once (prefer the **central model path**) and `docKey` (preferred). The Revit add-in auto-initializes this on view activation.

## Revit Add-in UI
- Ribbon: `RevitMCPServer` tab → `GUI` panel → `Chat`
- Invite: `Invite` posts to `ws://Project/Invites` with `@userId` mentions.
- Receiving invites: the add-in polls the inbox and shows a **non-blocking toast** in Revit.

## Codex GUI (Chat Monitor)
- Click `Chat` (top bar) → Chat Monitor (polls `chat.list`)
- Compliance note: Chat Monitor is read-only. It does **not** auto-run Codex and does **not** post AI replies back to chat.
- Shortcut: select a message → `Copy→Codex`
  - Copies the message text to clipboard and pastes it into the main Codex GUI prompt (does not execute).
  - The user clicks `Send` in Codex GUI when ready.
- `Copy` / `Copy All` is available in the chat UIs (Codex GUI and the Revit Chat pane).

## Minimal JSON-RPC examples
Post:
```json
POST /rpc/chat.post
{ "jsonrpc":"2.0","id":"1","params":{
  "docPathHint":"\\\\Server\\ProjectA\\Central.rvt",
  "docKey":"8d5bcfd8-1222-46da-b13b-23336aae66c5",
  "channel":"ws://Project/General",
  "text":"Hello @userB",
  "type":"note",
  "actor":{"type":"human","id":"userA","name":"User A"}
}}
```

List:
```json
POST /rpc/chat.list
{ "jsonrpc":"2.0","id":"1","params":{
  "docPathHint":"\\\\Server\\ProjectA\\Central.rvt",
  "docKey":"8d5bcfd8-1222-46da-b13b-23336aae66c5",
  "channel":"ws://Project/General",
  "limit":100
}}
```

Inbox (mentions):
```json
POST /rpc/chat.inbox.list
{ "jsonrpc":"2.0","id":"1","params":{
  "docPathHint":"\\\\Server\\ProjectA\\Central.rvt",
  "docKey":"8d5bcfd8-1222-46da-b13b-23336aae66c5",
  "channel":"ws://Project/Invites",
  "userId":"userB",
  "limit":50
}}
```
