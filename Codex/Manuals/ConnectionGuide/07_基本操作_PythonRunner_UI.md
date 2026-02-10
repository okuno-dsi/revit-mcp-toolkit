# Python Runner UI（NEW 2026-01-22）

目的: Revit 内から Python スクリプトを直接実行するための UI です。PowerShell など外部シェル不要で、作業メモや簡易スクリプトを安全に実行できます。

## 起動方法
- Revit のリボン（RevitMCP タブ）にある **Py** ボタン（Python Runner）をクリック。

## 既定の保存先
- `Projects/<RevitFileName>_<docKey>/python_script/`
- Save / Save As はこのフォルダを初期値にします。

## 動作のポイント
- **Save / Save As 時に dedent**（共通先頭空白の削除）を自動適用します。
- **機能コメント（1行目）**:
  - Save / Save As 時に、**1行目へ `# @feature: ...` を自動挿入**します（Feature 欄に入力した内容）。
  - Keywords 欄がある場合は **2行目に `# @keywords: ...`** を自動挿入します。
  - 既に `@feature/@keywords` 行がある場合は更新されます。
- **MCP コマンド強調表示**:
  - `rpc("element.copy_elements", ...)` や `{"method":"doc.get_project_info", ...}` のような **メソッド名**は、濃い茶色＋ボールドで強調表示します（視認性向上）。
- **Run 実行時**:
  - スクリプトを `run_YYYYMMDD_HHMMSS.py` として保存してから実行。
  - `http://127.0.0.1:<PORT>` と `--port <PORT>` の指定は、現在の RevitMCP ポートへ自動変換。
  - `/jsonrpc` が含まれている場合は `/rpc` に自動変換（互換性のため）。
  - 実行プロセスに `REVIT_MCP_PORT` を自動設定（Python 側で参照可能）。
- **出力ログ**:
  - 出力の最初にだけ `[HH:mm:ss]` を出力（行ごとには出さない）。
  - stderr は `ERR:` プレフィックスで表示。
  - 実行が `queued` だった場合は自動で `/job/{id}` をポーリングし、結果を追記。

## CodexGUI 連携（Python スクリプトの受け渡し）
- CodexGUI で生成した Python は **必ず ` ```python ``` ` ブロックのみ**が保存対象です。
- 保存先は **`Projects/<RevitFileName>_<docKey>/python_script/`** に統一されます。
- 先頭に `# @feature:` / `# @keywords:` が自動挿入されます（未指定の場合は空欄）。
- Python Runner 側は **自動読み込みしません**。必要に応じて **Load Codex** ボタンで最新スクリプトを読み込みます。

## 注意
- CodexGUI で生成した Python は **Load Codex** から取り込みます。

## Script Library（一覧・検索）
- Python Runner の **Library** ボタンから、保存済みスクリプトの一覧を開けます。
- 一覧は **ファイル名 / 機能（Feature） / キーワード / フォルダ** を表形式で表示します。
- **Keyword Filter** で部分一致フィルタ、**Sort by Keyword** でキーワード順ソート。
- **Open / Edit / Delete / Open Folder** が可能です。
  - **Edit** で Feature / Keywords を編集し、スクリプト先頭のコメント行（`# @feature:` / `# @keywords:`）を更新します。

## Script Roots（検索フォルダの管理）
- Library の **Roots...** から、検索対象フォルダを追加・削除できます。
- 既定の `Projects/<RevitFileName>_<docKey>/python_script/` は常に対象です（削除不可）。

## Python 実行環境の探索順
1. 環境変数 `REVIT_MCP_PYTHON_EXE`
2. Add-in フォルダ内 `python/python.exe` または `python310/python.exe`
3. PATH 上の `python.exe` / `py.exe`

## 注意
- 実行中の Python プロセスは **Stop** で強制終了できます。
- 出力ウィンドウは **Copy** で全文コピーできます。

