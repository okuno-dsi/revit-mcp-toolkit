## ExcelMCP 運用マニュアル（日本語）

本ドキュメントは ExcelMCP の起動・MCP 利用・主要 API・テスト・ビルド手順をまとめた運用マニュアルです。

### 1. 概要
- 役割: Excel ワークブックの読み取り、書き込み、追記、CSV/JSON 出力、書式設定、数式設定、グラフ一覧取得、COM 自動操作を HTTP API と MCP tool の両方で提供します。
- 実装: ASP.NET Core Minimal API、ClosedXML、OpenXML SDK。
- 既存機能: `/parse_plan` は継続維持しています。

### 2. 起動方法
1. .NET 8 SDK をインストールします。
2. 任意ポートで起動します。例:
   - `dotnet run --project ExcelMCP --configuration Release --urls http://localhost:5215`
3. 付属スクリプトでも起動できます。
   - 前面起動: `pwsh .\start.ps1 -Port 5215`
   - バックグラウンド起動: `pwsh .\start.ps1 -Port 5215 -Background`
   - 状態確認: `pwsh .\status.ps1`
   - 停止: `pwsh .\stop.ps1`
4. 正常起動の目印:
   - `[ExcelMCP] Building application...`
   - `Now listening on: http://localhost:5215`
   - `[ExcelMCP] Endpoints:` にルート一覧
5. ヘルスチェック:
   - `curl http://localhost:5215/health`

### 3. MCP 仕様
#### 3.1 サポートする protocol version
- 既定: `2025-11-25`
- 互換:
  - `2025-11-05`
  - `2025-03-26`
- `initialize` で未対応 version を受け取った場合、その値をそのまま返さず、サーバーが対応する version にネゴシエーションします。

#### 3.2 Transport
- `OPTIONS /mcp`
- `GET /mcp`
- `POST /mcp`
- `DELETE /mcp`

補足:
- `GET /mcp` は session 確立後のストリーム接続口です。
- `DELETE /mcp` は session を破棄します。
- `notifications/initialized` のような notification には JSON-RPC response を返しません。HTTP としては `202 Accepted` を返します。
- batch request（JSON array）を受け付けます。

#### 3.3 セッション手順
1. `initialize`
2. `notifications/initialized`
3. `tools/list`
4. `tools/call`

### 4. 主な MCP tools
#### 4.1 ファイル系 first-class tools
- `excel.health`
- `excel.parse_plan`
- `excel.sheet_info`
- `excel.read_cells`
- `excel.write_cells`
- `excel.append_rows`
- `excel.set_formula`
- `excel.format_sheet`
- `excel.to_csv`
- `excel.to_json`
- `excel.list_charts`

これらは `mcp_commands.jsonl` を基に registry 化されています。

補足:
- `excel.file.*` という別名も用意しています。
- ファイルが Excel で開かれていてロックされている場合、読み取り系は一時 live copy を使います。
- 書き込み系の一部は live workbook への COM フォールバックを行います。

#### 4.2 COM 系 tools
- `excel.list_open_workbooks`
- `excel.live.list_workbooks`
- `excel.live.read_cells`
- `excel.live.write_cells`
- `excel.live.append_rows`
- `excel.live.save_workbook`
- `excel.com.activate_workbook`
- `excel.com.activate_sheet`
- `excel.com.read_cells`
- `excel.com.write_cells`
- `excel.com.append_rows`
- `excel.com.save_workbook`
- `excel.com.format_range`
- `excel.com.add_sheet`
- `excel.com.delete_sheet`
- `excel.com.sort_range`

#### 4.3 補助 tools
- `excel.preview_write_cells`
- `excel.preview_append_rows`
- `excel.preview_set_formula`
- `excel.api_call`
- `mcp.status`

補足:
- `excel.api_call` は既存 endpoint への汎用 fallback です。新規クライアントは first-class tools を優先してください。
- preview tools は workbook を変更しません。書き込み前の確認用です。
- 使い分けは次の通りです。
  - 保存済みファイルを扱う: `excel.file.*` または `excel.*`
  - Excel で開いているブックを扱う: `excel.live.*` または `excel.com.*`

### 5. 主な HTTP エンドポイント
- `GET /health`
- `GET /list_open_workbooks`
- `POST /sheet_info`
- `POST /read_cells`
- `POST /write_cells`
- `POST /append_rows`
- `POST /set_formula`
- `POST /format_sheet`
- `POST /to_csv`
- `POST /to_json`
- `POST /list_charts`
- `POST /parse_plan`
- `/com/*` 配下の COM 操作エンドポイント

重要:
- `POST /com/read_cells` は workbook / worksheet を厳格に解決します。
- 複数ブックが開いている状態で `workbookFullName` / `workbookName` を省略すると 4xx を返します。
- `sheetName` を指定して見つからない場合も 4xx を返します。

### 6. テスト
#### 6.1 一括 integration test
- `dotnet test ExcelMCP.sln -c Release --no-build`
- 対象:
  - `initialize`
  - `notifications/initialized`
  - `tools/list`
  - `tools/call`
  - invalid session
  - unsupported protocol version
  - batch
  - `GET /mcp`
  - `OPTIONS /mcp`
  - `DELETE /mcp`

#### 6.2 既存 PowerShell 疎通テスト
- スクリプト: `ExcelMCP/test_requests.ps1`
- 実行例:
  - `pwsh ExcelMCP/test_requests.ps1 -BaseUrl http://localhost:5215`

### 7. Build / Release 手順
1. `dotnet clean ExcelMCP.sln`
2. `dotnet build ExcelMCP.sln -c Release`
3. `dotnet test ExcelMCP.sln -c Release --no-build`
4. 上記が通ったビルドだけを配布元に反映します。

簡易実行:
- `pwsh .\publish_release.ps1`
- clean/build/test/publish を一括実行し、`publish\Release` を正本出力にします。

重要:
- `bin/` と `obj/` は配布物の正本ではありません。
- 古い Release フォルダを source より優先しないでください。
- source と配布バイナリのズレを避けるため、必ず同じ source tree から build/test/publish を行ってください。

### 8. ログとデバッグ
- 保存先:
  - `%LOCALAPPDATA%\Revit_MCP\Logs\ExcelMCP`
  - `excelmcp-YYYYMMDD.log`
- PID:
  - `%LOCALAPPDATA%\Revit_MCP\Run\ExcelMCP.pid`
- 保持期間: 7日
- PowerShell 例:
  - `Get-Content "$env:LOCALAPPDATA\Revit_MCP\Logs\ExcelMCP\excelmcp-$(Get-Date -Format yyyyMMdd).log" -Tail 50`

### 9. ユーザーフレンドリー化の現状
- `tools/list` は generic な `excel.api_call` だけでなく、主要 Excel 操作を first-class tools として返します。
- input schema に required 項目を持ち、MCP 側でも最低限の引数検証を行います。
- preview tool により、書き込み系操作の事前確認が可能です。
- `mcp.status` で session と protocol negotiation 状態を確認できます。

### 10. 参考ファイル
- `README.md`
- `BUILD_RELEASE.md`
- `mcp_commands.jsonl`
- `Program.cs`
- `Mcp/` フォルダ配下の MCP 実装
