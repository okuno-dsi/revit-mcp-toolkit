# ExcelMCP 配置ガイド（Revit MCP 連携）

本書は ExcelMCP をどこに配置し、どのように参照・運用すべきかをまとめたものです。現行の ExcelMCP は Excel → 線分・ラベル抽出だけではなく、Excel 操作を MCP tools と HTTP API の両方で提供します。

---

## 結論
- Add-in フォルダ直下に 置かない のが基本方針です。ExcelMCP は「外部ユーティリティ／補助サービス」として、Add-in と分離した場所に配置するのが妥当です。
- Add-in からは `serviceUrl`（例: `http://localhost:5216`）または環境変数 `EXCEL_MCP_URL` で参照します。
- インストーラ配布では `Documents\\Revit_MCP\\Apps\\ExcelMCP\\` を正本配置とします。
- 配布先では `mcp_commands.jsonl` を同梱し、first-class MCP tool registry を有効にします。
- 配布先の運用文書は `Apps\\ExcelMCP\\README.md` と `Apps\\ExcelMCP\\MANUAL_JA.md` を正本とします。

---

## 推奨ディレクトリ構成
優先度順のおすすめパターンです。状況に合わせて選択してください。

- 開発（ローカル）
  - `C:\\Users\\<you>\\Documents\\VS2022\\ExcelMCP`
  - メリット: Add-in と同一マシンで管理・デバッグしやすい

- 運用（配布・本番）
  - `C:\\Users\\<you>\\Documents\\Revit_MCP\\Apps\\ExcelMCP\\`
  - メリット: Revit_MCP 配下で実行物・文書・設定の位置が一貫する
  - 同梱文書:
    - `README.md`
    - `MANUAL_JA.md`
    - `BUILD_RELEASE.md`

---

## 避けたい配置
- Add-in 配布フォルダ直下（例: `...\\RevitMCPAddin\\` の配下）
  - Add-in の読み込みパスに実行サービスの EXE/依存 DLL が混じると、読み込み失敗・故障時の切り分けが難しくなります。

---

## ポート／URL と接続
- 既定はループバック固定: `http://localhost:5216`
- Add-in / スクリプト側の参照順（推奨）
  1. 明示パラメータ `serviceUrl`
  2. 環境変数 `EXCEL_MCP_URL`
  3. 既定（fallback）
- ポート競合が起きたら ExcelMCP 側の起動引数／設定でポート変更（例: `--urls http://localhost:52xx`）。
- MCP の既定 protocol version は `2025-11-25`、互換 version は `2025-11-05` と `2025-03-26` です。
- 利用手順は `initialize` → `notifications/initialized` → `tools/list` / `tools/call` です。
- `tools/list` では generic な `excel.api_call` だけでなく、`excel.sheet_info` / `excel.read_cells` / `excel.write_cells` / `excel.append_rows` / `excel.set_formula` / `excel.format_sheet` / `excel.to_csv` / `excel.to_json` / `excel.list_charts` を first-class tools として取得できます。
- 書き込み前確認には `excel.preview_write_cells` / `excel.preview_append_rows` / `excel.preview_set_formula` を使います。

---

## ログと設定の分離
- ログ既定: `%TEMP%\\ExcelMCP\\logs\\`
- 設定既定: `Apps\\ExcelMCP\\appsettings.json` と `Apps\\ExcelMCP\\appsettings.Development.json`
- 運用で設定を分ける場合は、起動引数またはホスト側の環境変数で上書きする
- `mcp_commands.jsonl` は runtime 必須ファイルなので、EXE と同じディレクトリに置く

---

## バージョニングと配布
- ディレクトリにバージョンを付与（例: `ExcelMCP\\1.2.3\\`）
- `current` を張り替えるだけでロールバック可能
- Add-in と ExcelMCP は別バージョンでも連携できるよう API 後方互換を意識（`/parse_plan` のスキーマ安定化）

---

## 実行形態（設計アイデア）
- 常駐: Windows サービス化／タスクスケジューラのログオン時起動
- 一時起動: スクリプトから起動→処理→終了も可能だが、Revit と並走させるなら常駐の方が安定
- セキュリティ: ループバックのみ bind。外部公開は避ける（必要なら FW で制限）

---

## Add-in／スクリプトからの参照
- Add-in フォルダには実体を置かず、`serviceUrl`／`EXCEL_MCP_URL` の参照方法を README に記載
- スクリプトの参照順も同じ（パラメータ → 環境変数 → 既定）

---

## デバッグの小技
- ExcelMCP の `/parse_plan` 応答（`segmentsDetailed`／`labels`）をログ保存して Add-in と同じ原点補正で再現
- `/health` で生存監視。失敗時は `useColorMask` 切替やシート名の見直し
- `OPTIONS /mcp` で transport を確認し、`tools/list` で first-class tools が出ることを確認する
- `EXCEL_MCP_URL` 未設定の場合、ローカルの空きポート 5000–5400 を探索して自動使用する仕組みも有効（将来的な改善点）

---

## まとめ
- ExcelMCP は Add-in 配下ではなく、分離配置が原則。
- 開発: `C:\\Users\\<you>\\Documents\\VS2022\\ExcelMCP`、運用: `C:\\Users\\<you>\\Documents\\Revit_MCP\\Apps\\ExcelMCP` を推奨。
- Add-in／スクリプトからは `serviceUrl`（または `EXCEL_MCP_URL`）で参照し、`mcp_commands.jsonl` を含む配布単位で管理します。
- 配布物の確認は `README.md` / `MANUAL_JA.md` / `BUILD_RELEASE.md` の3点と、`/health` / `/mcp` の疎通で行います。
