# RevitMCP ⇄ AutoCadMCP Combined Quickstart (最短到達版)

目的
- RevitMCP からビュー内の「壁」をインスタンスパラメータ「コメント」で分類し、グループごとに DWG を書き出し、AutoCadMCP でレイヤー名をコメント別に再命名して1つのDWGに統合する。
- 次回起動時に最短でここまで到達できるよう、確実に動く最小手順とトラブル回避をまとめる。

前提
- Revit 起動＋MCPアドイン有効（既定ポート 5210）
- AutoCadMCP サーバー起動可能（既定ポート 5251）
- AutoCAD Core Console 2025 (accoreconsole.exe) 利用可能
- 作業フォルダ: `Work/AutoCadOut`

---

## 1) RevitMCP: 接続と最短チェック

最短動作確認（PowerShell）
- ポート確認: `Test-NetConnection localhost -Port 5210`
- 疎通＆ブートストラップ: `Manuals/Scripts/test_connection.ps1 -Port 5210`
  - ログ: `Work/<ProjectName>_<Port>/Logs/agent_bootstrap.json`

よく使う送信スクリプト
- `Manuals/Scripts/send_revit_command_durable.py`（JSON-RPC durable送信）
- 例: `python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command ping_server`

---

## 2) RevitMCP: 書き出し用ビューの準備（壁を確実に見せる）

推奨手順（コマンド）
1. ビュー作成＋活性化

```
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command create_view_plan --params '{"levelName":"レベル 1","name":"Export_NoTemplate","__smoke_ok":true}'
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command activate_view --params '{"viewId": <viewId>}'
```

2. テンプレート解除＋カテゴリ可視＋フィット

```
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command set_view_template --params '{"viewId": <viewId>, "clear": true, "__smoke_ok": true}'
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command save_view_state --params '{}'
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command view_fit --params '{"viewId": <viewId>}'
```

注意
- Revit が Viewer モードだと書き出し不可（エラー: Exporting is not allowed）。通常モードへ再起動。

---

## 3) 壁の抽出と「コメント」での分類

抽出

```
# ビュー内要素ID（idsOnly）
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command get_elements_in_view --params '{"viewId": <viewId>, "_shape": {"idsOnly": true, "page": {"limit": 20000}}}' --output-file Work/<ProjectName>_<Port>/Logs/elements_in_view.json

# 要素情報（カテゴリ判定用）
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command get_element_info --params '{"elementIds": [<ids...>], "rich": true}' --output-file Work/<ProjectName>_<Port>/Logs/elements_info.json
```

- 壁カテゴリID: `-2000011`

「コメント」の読み取り（インスタンス）

```
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command list_wall_parameters --params '{"elementId": <wallId>}' --output-file Work/<ProjectName>_<Port>/Logs/wall_<id>_params.json
```

- `name` が `Comments`/`コメント` に該当するパラメータの `value` または `display` を使用
- A/B/C/D 等へグループ化。空白は `NoComment` とする

---

## 4) グループごとに DWG へ書き出し

最小フロー

```
# あるグループ keep[] のみ残し、その他 allIds-keep を非表示
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command hide_elements_in_view --params '{"viewId": <viewId>, "elementIds": [<hide...>]}'

# As Displayed で DWG 書き出し（ACAD2018）
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command export_dwg --params '{"viewId": <viewId>, "outputFolder": "Work/AutoCadOut", "fileName": "walls_<COMMENT>", "dwgVersion": "ACAD2018", "__smoke_ok": true}'

# 解除
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command show_all_in_view --params '{"viewId": <viewId>, "detachViewTemplate": true, "includeTempReset": true}'
```

- 期待成果物: `Work/AutoCadOut/walls_A.dwg` ほか（A/B/C/D）

---

## 5) AutoCadMCP: 起動とヘルスチェック

起動
- 実行ファイル: `AutoCadMCP/AutoCadMcpServer/bin/Release/net8.0/AutoCadMcpServer.exe`（または Debug）

ヘルス

```
Invoke-RestMethod http://127.0.0.1:5251/health
```

注意（設定）
## 6) AutoCAD �����i2�p�^�[���j
- accoreconsole パス: 例 `C:/Program Files/Autodesk/AutoCAD 2025/accoreconsole.exe`
A) �ȒP�ɍŏ��m�点 per-file renaming �i��v�j
- �nAPI ��直�𑀍ݔ��Ȃ�ł��A�u�o�C���h�vDWG��存在確認��+�u失敗時のフォールバック�v��行う汎用スクリプトを使います。
- �T�|���v: `Manuals/Scripts/merge_dwgs_perfile_safe.py`
## 6) AutoCAD 統合（2パターン）
```bash
python Manuals/Scripts/merge_dwgs_perfile_safe.py ^
  --inputs C:/.../Work/AutoCadOut/walls_A.dwg C:/.../Work/AutoCadOut/walls_B.dwg ^
  --output C:/.../Work/AutoCadOut/merged_by_comment.dwg ^
  --seed C:/.../Work/AutoCadOut/SEED.dwg
```
$inputs = @(
- �w��概要
  - 1) AutoCadMCP �̎API `merge_dwgs_perfile_rename` ��呼び出し
  - 2) レスポンスの `ok` だけでなく、`output` パスに DWG が実在するかを確認
  - 3) もし DWG ��出来ていない場合は、`accoreconsole.exe /i <seed> /s <script>` ��直接呼び出し、INSERT+EXPLODE+レイヤリネーム+PURGE/AUDIT+SAVEAS 2018 で再実行
)
- サーバー側の補足
  - `MergeDwgsPerFileRenameHandler` �� staging �܂ł̑��؂�DWG (`final.dwg`) ��存在しない場合、`ok=false` / `Error="E_NO_OUTPUT_DWG"` ��返すよう修正済みです。
  - これにより「サーバーが OK を返しているのに DWG がない」という状態を防げます。
  accore=@{ path='C:/Program Files/Autodesk/AutoCAD 2025/accoreconsole.exe'; seed=$inputs[0].path; locale='en-US'; timeoutMs=600000 };
  postProcess=@{ layTransDws=$null; purge=$true; audit=$true };
  stagingPolicy=@{ root='C:/.../Work/AutoCadOut/Staging'; keepTempOnError=$true; atomicWrite=$true }
} } | ConvertTo-Json -Depth 20
Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:5251/rpc' -Body $rpc -ContentType 'application/json; charset=utf-8'
```

- 既知の罠: Core Console が LAYMRG 確認で待機しタイムアウトする環境あり（E_ACCORE_TIMEOUT）。

B) DXF経由（より安定・推奨、要 TrustedPaths）
- スクリプト（管理者推奨）: `Work/Tools/Run_MergeByDXF.ps1`
  - 変換: -SAVEAS/-DXFOUT で DWG→DXF（2018）
  - 置換: DXFテキスト内のレイヤ名 `A-WALL-____-MCUT` を `A-WALL-____-MCUT_<stem>` に一括置換
  - 統合: DXFIN で順次取り込み → PURGE/AUDIT → SAVEAS (2018)
- 実行例

```
pwsh -File Work/Tools/Run_MergeByDXF.ps1 -SourceDir Work/AutoCadOut -OutDir C:/Temp/CadOut -LayerName "A-WALL-____-MCUT" -AccorePath "C:/Program Files/Autodesk/AutoCAD 2025/accoreconsole.exe" -Locale en-US
```

- 事前に TrustedPaths を AutoCAD に設定（GUI: オプション→ファイル→信頼できる位置）
  - 例: `C:\Temp\CadOut; C:\Users\okuno\Documents\VS2022\Ver421\Codex\Work\AutoCadOut`

---

## 7) トラブルシュート（頻出）

- Revit 側: `Exporting is not allowed...` → Viewer モード解除、通常モードへ再起動
- AutoCAD 側: `E_ACCORE_TIMEOUT` → LAYMRG で待機。DXF経由に切替 or TrustedPaths/SECURELOAD 調整
- DXF 未生成: `DXF not produced` → 管理者で実行、TrustedPaths 追加、保存先パスの権限/AV除外を確認
- パスガード: 入出力ドライブ許可（`AutoCadMcpServer/appsettings.json` の `AllowedDrives`）

---

## 8) 次回最短到達のチェックリスト

1. RevitMCP 疎通
   - `Manuals/Scripts/test_connection.ps1 -Port 5210` → OK
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
   - 安定: `Work/Tools/Run_MergeByDXF.ps1`（TrustedPaths 追加済で）

---

## 参考（本リポジトリ内ファイル）
- Revit クイック: `Manuals/ConnectionGuide/QUICKSTART.md`
- スクリプト一覧: `Manuals/Scripts/README.md`
- 送信: `Manuals/Scripts/send_revit_command_durable.py`
- 便利スクリプト（本件向け）
  - `Work/Tools/Run_MergeByDXF.ps1`
  - `Work/Tools/ConvertToDxfOutDir.ps1`

以上。これに沿って順に実行すれば、次回起動時も最短で統合まで到達できます。

