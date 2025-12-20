## ExcelMCP 運用マニュアル（日本語）

本ドキュメントは ExcelMCP の起動・テスト・主要APIとデバッグ方法をまとめたマニュアルです。

### 1. 概要
- 役割: Excel ワークブックの基本操作を HTTP API で提供（読み出し・記入・追記・CSV 変換・フォーマット・数式・簡易グラフ一覧）。
- 既存機能: `/parse_plan`（罫線=壁・文字列=ラベルの抽出）を保持。
- 実装: ASP.NET Core Minimal API、ClosedXML、OpenXML SDK（グラフは読み取りのみ）。

### 2. 起動方法
1) .NET 8 SDK をインストール済みであること。
2) ポートを指定して起動（例: 5215）
   - `dotnet run --project ExcelMCP --configuration Release --urls http://localhost:5215`
3) 正常起動の目印
   - コンソールに以下が出力されます。
     - `[ExcelMCP] Building application...`
     - `Now listening on: http://localhost:5215`
     - `[ExcelMCP] Endpoints:` にルートと各 API 一覧
4) ヘルスチェック
   - `curl http://localhost:5215/health`
   - 200 が返れば正常です。

補足:
- PowerShell スクリプトから起動する場合は `ExcelMCP/start.ps1` で `-Port` を指定可能です。

### 3. 主要エンドポイント（抜粋）
- `GET /health` サーバーヘルス
- `POST /sheet_info` 既存ブックのシート一覧と used range 情報
- `POST /read_cells` A1 範囲または used range の読み取り
- `POST /write_cells` 指定セルから 2D 配列を書き込み
- `POST /append_rows` 最終行の下に行追加
- `POST /set_formula` A1 形式の数式をセル/範囲へ設定
- `POST /format_sheet` 列/行の自動調整・幅/高さ設定
- `POST /to_csv` シートを CSV へ出力（`encodingName` で文字コード指定）
- `POST /list_charts` グラフ一覧（シート名とタイトル）
- 既存: `POST /parse_plan` 罫線/ラベル抽出（既存 Revit 連携用途）

詳細なパラメータは `ExcelMCP/README.md` を参照してください。

### 4. 一括テスト
- スクリプト: `ExcelMCP/test_requests.ps1`
- 実行例:
  - `pwsh ExcelMCP/test_requests.ps1 -BaseUrl http://localhost:5215`
  - 既存の `.xlsx` をリポジトリから 1 つ選び、テンポラリへコピーして以下を順に実行します。
    - `/sheet_info` → `/write_cells` → `/append_rows` → `/set_formula` → `/format_sheet` → `/to_csv` → `/read_cells` → `/list_charts`
  - 全て `[OK]` が出力されればテスト合格です。

### 5. ログとデバッグ
- 起動ログ
  - 起動フェーズ・環境名・バインド URL・登録エンドポイント一覧を出力。
- リクエストログ
  - すべての HTTP リクエストで `[REQ]`（受信）/`[RES]`（応答）を出力。ステータスコードと経過時間(ms)付き。
  - 例外発生時は `[ERR]` に例外種別とメッセージを出力。
- 保存先（自動ローテーション）
  - コンソール出力に加えて、以下にも保存されます。
    - ディレクトリ: `%TEMP%\ExcelMCP\logs`
    - ファイル名: `excelmcp-YYYYMMDD.log`
  - 保持期間: 7日（起動時に 7 日より古い `.log` を自動削除）
  - 例: `C:\Users\<ユーザー>\AppData\Local\Temp\ExcelMCP\logs\excelmcp-20250101.log`
- 確認例（PowerShell）
  - 末尾確認: ``Get-Content "$env:TEMP\ExcelMCP\logs\excelmcp-$(Get-Date -Format yyyyMMdd).log" -Tail 50``
  - 最新ファイルの末尾: ``$d = Join-Path $env:TEMP 'ExcelMCP\logs'; Get-ChildItem $d -Filter 'excelmcp-*.log' | Sort-Object LastWriteTime | Select-Object -Last 1 | % { Get-Content $_.FullName -Tail 50 }``
- 代表的な注意点
  - `/to_csv` では `encodingName`（例: `utf-8`, `shift_jis`）を使用してください（`Encoding` 型は JSON で扱えません）。
  - 書き込み系は既存セルを上書きします。`append_rows` は空行の下ではなく「最後の使用行 +1 行目」に追記します。

### 6. MCP コマンド仕様
- ファイル: `ExcelMCP/mcp_commands.jsonl`
  - 各コマンドが 1 行の JSON で定義されています（`name`, `method`, `path`, `description`, `input_schema`, `output_example`）。
  - `/to_csv` の入力に `encodingName` を含める点に注意してください。

### 7. 既存機能との互換性
- 既存の `/parse_plan` は変更せず維持しています。Revit 連携ワークフローは従来通り利用できます。

### 8. トラブルシュート
- 起動直後に応答がない / 例外が発生する
  - コンソールの `[ERR]` または開発者例外ページ（Development 環境）を確認。
  - パッケージの競合がある場合は `dotnet restore` → `dotnet build -c Release` で確認。
- ポートが使用中
  - 別のポートに変更して `--urls` を指定して起動してください（例: `http://localhost:5220`）。
