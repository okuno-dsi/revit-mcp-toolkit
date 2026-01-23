# スクリーンショット取得 + 同意ゲート（Codex GUI）

RevitMCP の `capture.*` を使ってスクリーンショットを取得し、**Codex に送る前に必ず人間が確認して同意**するための手順です。

## 何が安全なのか（既定で安全）
- スクリーンショットは **ローカル**で取得（Revit 外部プロセス `CaptureAgent`）。
- Codex に画像を添付する前に **プレビュー + 明示同意**が必須（`codex --image ...`）。
- `risk: high`（図面/モデル表示が含まれる可能性が高い）を選ぶ場合は **既定で未選択**かつ追加確認が出ます。

## 前提
- `RevitMCPServer` が起動している（`capture.*` は server-local）。
- サーバーの出力配下に `capture-agent\\RevitMcp.CaptureAgent.exe` が同梱されている。
- Codex GUI が `%LOCALAPPDATA%\\RevitMCP\\server_state.json` からポートを取得できる。

## 手順（Codex GUI）
1. Codex GUI を開く
2. 上部の `Capture` をクリック
3. `Capture (Consent Gate)` ウィンドウで:
   - `Target` を選択
     - 推奨: `Revit dialogs (active_dialogs)`（エラー/警告向け）
     - `Revit main window` / `floating_windows` / `Full screen` は図面/モデル表示が含まれやすく **高リスク**
   - `Capture` をクリック
4. 一覧とプレビューで内容を確認
   - 送ってよいものだけチェックする
   - 必要なら `Open` で標準の画像ビューアで確認する
5. `Approve Selected` をクリック
   - `risk: high` が含まれる場合は追加の確認が出ます（既定は No）。
6. そのまま通常どおり Codex を実行（Codex GUI の `Enter`）
   - 承認した画像は **次の 1 回だけ**添付されます。

## 補足
- 既定の保存先:
  - PNG: `%LOCALAPPDATA%\\RevitMCP\\captures\\`
  - ログ(JSONL): `%LOCALAPPDATA%\\RevitMCP\\logs\\capture.jsonl`
- サーバーは画像を Codex に自動送信しません（同意ゲートは Codex GUI 側）。

## トラブルシュート
- Codex GUI が落ちる / 原因が分からない
  - `%LOCALAPPDATA%\\RevitMCP\\logs\\codexgui.log` を確認してください（未処理例外もここに残します）。
  - `codexgui.log` が大きすぎる場合、Codex GUI は 10MB 超で `codexgui_yyyyMMdd_HHmmss.log` にローテーションします（最新版）。
- `ERR: CaptureAgent failed.` / `CAPTURE_AGENT_EXIT_NONZERO`
  - `RevitMCPServer` 配下に `server\\capture-agent\\RevitMcp.CaptureAgent.exe` **と** `RevitMcp.CaptureAgent.dll` があることを確認してください。
  - 旧版のサーバーは `server\\RevitMcp.CaptureAgent.exe`（直下）を優先して起動し、直下に `.dll` が無いと失敗することがありました。最新版では `capture-agent\\` を優先します。
- `NO_REVIT_WINDOWS`
  - Revit が起動していない、または可視の Revit ウィンドウが見つからない状態です。Revit を前面表示するか、`Target=Full screen`（screen）で取得してください。
