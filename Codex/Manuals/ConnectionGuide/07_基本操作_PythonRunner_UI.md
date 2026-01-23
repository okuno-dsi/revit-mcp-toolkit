# Python Runner UI（NEW 2026-01-22）

目的: Revit 内から Python スクリプトを直接実行するための UI です。PowerShell など外部シェル不要で、作業メモや簡易スクリプトを安全に実行できます。

## 起動方法
- Revit のリボン（RevitMCP タブ）にある **Py** ボタン（Python Runner）をクリック。

## 既定の保存先
- `Work/<RevitFileName>_<docKey>/python_script/`
- Save / Save As はこのフォルダを初期値にします。

## 動作のポイント
- **Save / Save As 時に dedent**（共通先頭空白の削除）を自動適用します。
- **MCP コマンド強調表示**:
  - `rpc("element.copy_elements", ...)` や `{"method":"doc.get_project_info", ...}` のような **メソッド名**は、濃い茶色＋ボールドで強調表示します（視認性向上）。
- **Run 実行時**:
  - スクリプトを `run_YYYYMMDD_HHMMSS.py` として保存してから実行。
  - `http://127.0.0.1:<PORT>` と `--port <PORT>` の指定は、現在の RevitMCP ポートへ自動変換。
  - `/jsonrpc` が含まれている場合は `/rpc` に自動変換（互換性のため）。
- **出力ログ**:
  - 出力の最初にだけ `[HH:mm:ss]` を出力（行ごとには出さない）。
  - stderr は `ERR:` プレフィックスで表示。
  - 実行が `queued` だった場合は自動で `/job/{id}` をポーリングし、結果を追記。

## Python 実行環境の探索順
1. 環境変数 `REVIT_MCP_PYTHON_EXE`
2. Add-in フォルダ内 `python/python.exe` または `python310/python.exe`
3. PATH 上の `python.exe` / `py.exe`

## 注意
- 実行中の Python プロセスは **Stop** で強制終了できます。
- 出力ウィンドウは **Copy** で全文コピーできます。
