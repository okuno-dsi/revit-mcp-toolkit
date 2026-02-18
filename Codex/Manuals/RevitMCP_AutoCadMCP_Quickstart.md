# RevitMCP ⇄ AutoCadMCP Combined Quickstart (最短到達版)

目的
- RevitMCP からビュー内の「壁」をインスタンスパラメータ「コメント」で分類し、グループごとに DWG を書き出し、AutoCadMCP でレイヤー名をコメント別に再命名して1つのDWGに統合する。
- 次回起動時に最短でここまで到達できるよう、確実に動く最小手順とトラブル回避をまとめる。

前提
- Revit 起動＋MCPアドイン有効（既定ポート 5210）
- AutoCadMCP サーバー起動可能（既定ポート 5251）
- AutoCAD Core Console 2026 (accoreconsole.exe) 利用可能
- 作業フォルダ: `Projects/AutoCadOut`

---

## 1) RevitMCP: 接続と最短チェック

最短動作確認（PowerShell）
- ポート確認: `Test-NetConnection localhost -Port 5210`
- 疎通＆ブートストラップ: `Scripts/Reference/test_connection.ps1 -Port 5210`
  - ログ: `Projects/<ProjectName>_<Port>/Logs/agent_bootstrap.json`

よく使う送信スクリプト
- `Scripts/Reference/send_revit_command_durable.py`（JSON-RPC durable送信）
- 例: `python Scripts/Reference/send_revit_command_durable.py --port 5210 --command ping_server`

---

## 2) RevitMCP: 書き出し用ビューの準備（壁を確実に見せる）

推奨手順（コマンド）
1. ビュー作成＋活性化

```
python Scripts/Reference/send_revit_command_durable.py --port 5210 --command create_view_plan --params '{"levelName":"レベル 1","name":"Export_NoTemplate","__smoke_ok":true}'
python Scripts/Reference/send_revit_command_durable.py --port 5210 --command activate_view --params '{"viewId": <viewId>}'
```

2. テンプレート解除＋カテゴリ可視＋フィット

```
python Scripts/Reference/send_revit_command_durable.py --port 5210 --command set_view_template --params '{"viewId": <viewId>, "clear": true, "__smoke_ok": true}'
python Scripts/Reference/send_revit_command_durable.py --port 5210 --command save_view_state --params '{}'
python Scripts/Reference/send_revit_command_durable.py --port 5210 --command view_fit --params '{"viewId": <viewId>}'
```

注意
- Revit が Viewer モードだと書き出し不可（エラー: Exporting is not allowed）。通常モードへ再起動。

---

## 3) 壁の抽出と「コメント」での分類

抽出

```
# ビュー内要素ID（idsOnly）
python Scripts/Reference/send_revit_command_durable.py --port 5210 --command get_elements_in_view --params '{"viewId": <viewId>, "_shape": {"idsOnly": true, "page": {"limit": 20000}}}' --output-file Projects/<ProjectName>_<Port>/Logs/elements_in_view.json

# 要素情報（カテゴリ判定用）
python Scripts/Reference/send_revit_command_durable.py --port 5210 --command get_element_info --params '{"elementIds": [<ids...>], "rich": true}' --output-file Projects/<ProjectName>_<Port>/Logs/elements_info.json
```

- 壁カテゴリID: `-2000011`

「コメント」の読み取り（インスタンス）

```
python Scripts/Reference/send_revit_command_durable.py --port 5210 --command list_wall_parameters --params '{"elementId": <wallId>}' --output-file Projects/<ProjectName>_<Port>/Logs/wall_<id>_params.json
```

- `name` が `Comments`/`コメント` に該当するパラメータの `value` または `display` を使用
- A/B/C/D 等へグループ化。空白は `NoComment` とする

---

## 4) グループごとに DWG へ書き出し

最小フロー

```
# あるグループ keep[] のみ残し、その他 allIds-keep を非表示
python Scripts/Reference/send_revit_command_durable.py --port 5210 --command hide_elements_in_view --params '{"viewId": <viewId>, "elementIds": [<hide...>]}'

# As Displayed で DWG 書き出し（ACAD2018）
python Scripts/Reference/send_revit_command_durable.py --port 5210 --command export_dwg --params '{"viewId": <viewId>, "outputFolder": "Projects/AutoCadOut", "fileName": "walls_<COMMENT>", "dwgVersion": "ACAD2018", "__smoke_ok": true}'

# 解除
python Scripts/Reference/send_revit_command_durable.py --port 5210 --command show_all_in_view --params '{"viewId": <viewId>, "detachViewTemplate": true, "includeTempReset": true}'
```

- 期待成果物: `Projects/AutoCadOut/walls_A.dwg` ほか（A/B/C/D）

---

## 5) AutoCadMCP: 起動とヘルスチェック

起動
- 実行ファイル: `AutoCadMCP/AutoCadMcpServer/bin/Release/net8.0/AutoCadMcpServer.exe`（または Debug）

ヘルス

```
Invoke-RestMethod http://127.0.0.1:5251/health
```

注意（設定）
## 6) AutoCAD 統合（3パターン）

A) AutoCadMCP（accoreconsole 直叩き）
- accoreconsole パス例: `C:/Program Files/Autodesk/AutoCAD 2026/accoreconsole.exe`
- サーバー API `merge_dwgs_perfile_rename` を使い、**DWG 実在確認＋失敗時フォールバック**まで行う。
- サンプル: `Scripts/Reference/merge_dwgs_perfile_safe.py`

```bash
python Scripts/Reference/merge_dwgs_perfile_safe.py ^
  --inputs C:/.../Projects/AutoCadOut/walls_A.dwg C:/.../Projects/AutoCadOut/walls_B.dwg ^
  --output C:/.../Projects/AutoCadOut/merged_by_comment.dwg ^
  --seed C:/.../Projects/AutoCadOut/SEED.dwg
```

- 概要
  - 1) AutoCadMCP API `merge_dwgs_perfile_rename` を呼び出し
  - 2) レスポンスの `ok` だけでなく、`output` に DWG が実在するか確認
  - 3) DWG が無い場合は、`accoreconsole.exe /i <seed> /s <script>` で再実行（INSERT+EXPLODE+レイヤリネーム+PURGE/AUDIT+SAVEAS 2018）
- 既知の罠: Core Console が LAYMRG 確認で待機しタイムアウトする環境あり（E_ACCORE_TIMEOUT）。

B) DXF経由（より安定・推奨、要 TrustedPaths）
- スクリプト（管理者推奨）: `Tools/AutoCad/Run_MergeByDXF.ps1`
  - 変換: -SAVEAS/-DXFOUT で DWG→DXF（2018）
  - 置換: DXFテキスト内のレイヤ名 `A-WALL-____-MCUT` を `A-WALL-____-MCUT_<stem>` に一括置換
  - 統合: DXFIN で順次取り込み → PURGE/AUDIT → SAVEAS (2018)
- 実行例

```
pwsh -File Tools/AutoCad/Run_MergeByDXF.ps1 -SourceDir Projects/AutoCadOut -OutDir C:/Temp/CadOut -LayerName "A-WALL-____-MCUT" -AccorePath "C:/Program Files/Autodesk/AutoCAD 2026/accoreconsole.exe" -Locale ja-JP
```

- 事前に TrustedPaths を AutoCAD に設定（GUI: オプション→ファイル→信頼できる位置）
  - 例: `C:\Temp\CadOut; %USERPROFILE%\Documents\Revit_MCP\Projects\AutoCadOut`

C) COM経由（AutoCAD起動中、最も直感的）
- スクリプト: `Tools/AutoCad/merge_dwgs_by_map_com.py`
- 依存ライブラリ: `pywin32`（AutoCAD COM 用）
  - AI エージェントが必要に応じてインストール支援します:  
    `python -m pip install pywin32`
  - その他の追加ライブラリは不要（標準ライブラリのみ）。
- 例（DWG統合＋レイヤマップ適用）:

```
python Tools/AutoCad/merge_dwgs_by_map_com.py ^
  --source-dir C:/.../Projects/dwg ^
  --out-dwg C:/.../Projects/dwg/MERGED_DWG_COM.dwg ^
  --map-csv C:/.../Projects/dwg/layermap.csv
```

- 注意:
  - 出力先 DWG が AutoCAD で開いていると上書きできません。
  - AutoCAD を起動した状態で実行します（COM 経由）。

---

## 7) トラブルシュート（頻出）

- Revit 側: `Exporting is not allowed...` → Viewer モード解除、通常モードへ再起動
- AutoCAD 側: `E_ACCORE_TIMEOUT` → LAYMRG で待機。DXF経由に切替 or TrustedPaths/SECURELOAD 調整
- DXF 未生成: `DXF not produced` → 管理者で実行、TrustedPaths 追加、保存先パスの権限/AV除外を確認
- パスガード: 入出力ドライブ許可（`AutoCadMcpServer/appsettings.json` の `AllowedDrives`）

---

## 8) 次回最短到達のチェックリスト

1. RevitMCP 疎通
   - `Scripts/Reference/test_connection.ps1 -Port 5210` → OK
2. ビュー準備（壁可視）
   - `create_view_plan` → `activate_view` → `set_view_template(clear)` → `set_category_visibility(-2000011,true)` → `view_fit`
3. 壁抽出/分類
   - `get_elements_in_view(idsOnly)` → `get_element_info(rich)` → `list_wall_parameters` で A/B/C/D
4. DWG 書出し
   - グループごとに `hide_elements_in_view` → `export_dwg` → `reset_all_view_overrides`
5. AutoCadMCP 起動＋ヘルス
   - `Invoke-RestMethod http://127.0.0.1:5251/health`
6. 統合
   - 直接: `merge_dwgs_perfile_rename`（include=`A-WALL-____-MCUT`）
   - 安定: `Tools/AutoCad/Run_MergeByDXF.ps1`（TrustedPaths 追加済で）

---

## 参考（本リポジトリ内ファイル）
- Revit クイック: `Manuals/ConnectionGuide/QUICKSTART.md`
- スクリプト一覧: `Scripts/Reference/README.md`
- 送信: `Scripts/Reference/send_revit_command_durable.py`
- 便利スクリプト（本件向け）
  - `Tools/AutoCad/Run_MergeByDXF.ps1`
  - `Tools/AutoCad/ConvertToDxfOutDir.ps1`

以上。これに沿って順に実行すれば、次回起動時も最短で統合まで到達できます。





