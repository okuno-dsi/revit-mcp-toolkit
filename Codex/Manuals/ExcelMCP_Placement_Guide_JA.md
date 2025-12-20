# ExcelMCP 配置ガイド（Revit MCP 連携）

本書は ExcelMCP（Excel → 線分・ラベル抽出 API）をどこに配置し、どのように参照・運用すべきかをまとめたものです。Revit のアドイン配下に置くか？という論点も含め、だれでも同じ方針で扱えるようにします。

---

## 結論
- Add-in フォルダ直下に 置かない のが基本方針です。ExcelMCP は「外部ユーティリティ／補助サービス」として、Add-in と分離した場所に配置するのが妥当です。
- Add-in からは `serviceUrl`（例: `http://localhost:5216`）または環境変数 `EXCEL_MCP_URL` で参照します。

---

## 推奨ディレクトリ構成
優先度順のおすすめパターンです。状況に合わせて選択してください。

- 開発（ローカル）
  - `C:\\Users\\<you>\\Documents\\VS2022\\ExcelMCP`（現状の構成でOK）
  - メリット: Add-in と同一マシンで管理・デバッグしやすい

- リポジトリ内のサービス階層
  - `<repoRoot>\\Tools\\Services\\ExcelMCP`
  - メリット: Add-in と同じ Git でバージョン管理可能
  - 注意: Revit の読み込み対象（Add-in の DLL 群）とは混在させない

- 運用（配布・本番）
  - `C:\\RevitMCP\\Services\\ExcelMCP\\<version>\\`
  - `current` シンボリックリンク or ショートカットでバージョン切替
  - メリット: リリースとロールバックが容易、権限やFW設定を集約しやすい

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

---

## ログと設定の分離
- ログ: `C:\\RevitMCP\\Logs\\ExcelMCP\\` に集約（掃除や権限管理が容易）
- 設定: `C:\\RevitMCP\\Config\\` に配置し、`appsettings.Development.json` と運用用を分離
- リポジトリにはテンプレートと README のみをコミットし、実運用値はローカル配置

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
- `EXCEL_MCP_URL` 未設定の場合、ローカルの空きポート 5000–5400 を探索して自動使用する仕組みも有効（将来的な改善点）

---

## まとめ
- ExcelMCP は Add-in 配下ではなく、分離配置が原則。
- 開発: `C:\\Users\\<you>\\Documents\\VS2022\\ExcelMCP`、運用: `C:\\RevitMCP\\Services\\ExcelMCP\\<version>` を推奨。
- Add-in／スクリプトからは `serviceUrl`（または `EXCEL_MCP_URL`）で参照し、ビルド／配布／障害切り分けの境界を明確化します。
