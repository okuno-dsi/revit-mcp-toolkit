# ChangeLog 2026-01-09 (Server: stale RUNNING cleanup + docs endpoints canonical-only defaults / Add-in: create_flush_walls)

## 変更点（要約）
- `revit.status` のキュー統計で `RUNNING` が残留して誤認される問題に対応しました。
  - 極端に古い `RUNNING/DISPATCHING` を `DEAD`（`error_code:"STALE"`）として回収（best-effort）。
  - 既定しきい値は 6 時間（`REVIT_MCP_STALE_INPROGRESS_SEC` で変更可）。
  - 応答に `staleCleanup`（`staleAfterSec`, `reclaimedCount`）を追加。
  - サーバー起動後も 10 分ごとに回収を試行（best-effort）。
- コマンド一覧の混乱（deprecated と非deprecatedが混在して見える）を避けるため、サーバーの docs 系エンドポイントを canonical-only 既定に変更しました。
  - `GET /docs/manifest.json` は既定で deprecated alias を除外（`?includeDeprecated=1` で全件）。
  - `GET /debug/capabilities` は既定で deprecated alias を除外（`?includeDeprecated=1` で全件）。
  - `GET /debug/capabilities?includeDeprecated=1&grouped=1` で canonical→aliases の関係を一覧表示。
- `/debug/capabilities` の legacy→canonical 解決を堅牢化しました（“末尾一致”で辿れないリネーム系の alias を確実に解決）。
- Add-in: `element.create_flush_walls`（alias: `create_flush_walls`）を追加しました（既存壁に密着（面合わせ）する壁を別タイプで作成）。
- Add-in: `room.apply_finish_wall_type_on_room_boundary`（alias: `apply_finish_wall_type_on_room_boundary`）を追加しました（部屋境界セグメント長で仕上げ壁を作成）。
  - 追記: `includeBoundaryColumns=true`（既定）で、境界要素が柱（構造柱/建築柱）のセグメントも対象にできます（柱が Room Bounding として境界に出ている場合）。

## 影響するコマンド/エンドポイント
- JSON-RPC: `revit.status`（alias: `status`, `revit_status`）
- JSON-RPC: `element.create_flush_walls`（alias: `create_flush_walls`）
- JSON-RPC: `room.apply_finish_wall_type_on_room_boundary`（alias: `apply_finish_wall_type_on_room_boundary`）
- HTTP:
  - `GET /docs/manifest.json`（`includeDeprecated` 対応）
  - `GET /debug/capabilities`（`includeDeprecated`/`grouped` 対応）

## 変更ファイル
- `RevitMCPServer/Program.cs`
- `RevitMCPServer/Engine/DurableQueue.cs`
- `RevitMCPServer/Docs/CapabilitiesGenerator.cs`
- `RevitMCPAddin/Commands/ElementOps/Wall/CreateFlushWallsCommand.cs`
- `RevitMCPAddin/Core/Walls/WallFlushPlacement.cs`
- `RevitMCPAddin/Models/CreateFlushWallsRequest.cs`
- `RevitMCPAddin/Models/CreateFlushWallsResponse.cs`
- `RevitMCPAddin/Commands/Room/ApplyFinishWallsOnRoomBoundaryCommand.cs`
- `RevitMCPAddin/RevitMcpWorker.cs`
- `RevitMCPAddin/RevitMCPAddin.csproj`
- `Codex/Manuals/AGENT_README.md`
- `Codex/Manuals/ConnectionGuide/Startup_Manifest_JA.md`
- `Codex/Manuals/ConnectionGuide/06_基本操作_壁の作成.md`
- `Codex/Manuals/FullManual/create_flush_walls.md`
- `Codex/Manuals/FullManual/apply_finish_wall_type_on_room_boundary.md`
- `Codex/Manuals/FullManual/server_docs_endpoints.md`
- `Codex/Manuals/FullManual/README.md`
- `Codex/Manuals/FullManual/README_by_feature.md`
- `Codex/Manuals/FullManual/README_by_verb.md`
- `Codex/Manuals/FullManual_ja/create_flush_walls.md`
- `Codex/Manuals/FullManual_ja/apply_finish_wall_type_on_room_boundary.md`
- `Codex/Manuals/FullManual_ja/server_docs_endpoints.md`
- `Codex/Manuals/FullManual_ja/README.md`
- `Codex/Manuals/FullManual_ja/README_by_feature.md`
- `Codex/Manuals/FullManual_ja/README_by_verb.md`
- `Codex/Manuals/FullManual/revit_status.md`
- `Codex/Manuals/FullManual_ja/revit_status.md`
- `Codex/Manuals/UpdateLog.md`
