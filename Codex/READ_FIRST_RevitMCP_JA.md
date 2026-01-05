# READ FIRST: RevitMCP 120% クイックスタート

この1枚だけ読めば、Revit MCP サーバーへ確実に接続・取得・安全な書き込みまで行えます。必要なコマンドはそのまま貼り付けて使えます。

## 0) 前提と既定
- Revit が起動し、MCP Add-in が稼働していること（既定ポートは `5210`）。
- PowerShell 5+/7+、Python 3.x が利用可能。
- 既定ポート上書き: 環境変数 `REVIT_MCP_PORT` または各スクリプトの `-Port`/`--port` 引数。

## 1) 60秒の接続チェック（必須）
1) ポート疎通
```
Test-NetConnection localhost -Port 5210
```
2) ブートストラップ（環境情報の取得と保存）
```
pwsh -File Manuals/Scripts/test_connection.ps1 -Port 5210
```
- 出力: `Work/<ProjectName>_<Port>/Logs/agent_bootstrap.json`
- アクティブビューID: `result.result.environment.activeViewId`
3) Python クライアントでの疎通（任意・推奨）
```
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command ping_server
```

### 1.5) 起動時のコマンド走査と登録（自動）
- Add-in は起動直後に、実装クラス（`CommandName` と `Execute(...)` を持つハンドラ）をアセンブリ内から一度だけ走査し、ローカルHTTPサーバへコマンドマニフェストを登録します。
- 実装: `Manifest/ManifestExporter.cs` が反射で抽出 → `POST http://127.0.0.1:<PORT>/manifest/register` へ送信。`App.cs` の `OnStartup` から非同期で最大5回まで再試行します（ベストエフォート）。
- ランタイムに利用できる実コマンドの一覧は、マニフェストとは独立して `list_commands` でいつでも取得できます。

取得例（名前だけの簡易一覧）:
```
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command list_commands --params '{"namesOnly":true}' --output-file Work/<ProjectName>_<Port>/Logs/list_commands_names.json
```
出力: `result.result.commands` にメソッド名の配列


## 2) 読み取りの最小セット（安全）
- アクティブビュー内の要素ID一覧（PowerShell）
```
pwsh -File Manuals/Scripts/list_elements_in_view.ps1 -Port 5210
```
- 出力: `Work/<ProjectName>_<Port>/Logs/elements_in_view.json`（IDは `result.result.rows`）
- Python で直接取得（IDs only, 上限200件）
```
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command get_elements_in_view --params "{\"viewId\": <activeViewId>, \"_shape\":{\"idsOnly\":true,\"page\":{\"limit\":200}}}" --output-file Work/<ProjectName>_<Port>/Logs/elements_in_view.json
```

## 3) 書き込みは「安全スクリプト」で（smoke_test → 実行）
1) 可視化の一時上書き（色・透明度）
```
# まずは DryRun で送信内容を確認
pwsh -File Manuals/Scripts/set_visual_override_safe.ps1 -Port 5210 -ElementId <id> -DryRun

# 問題なければ実行（必要に応じて -Force）
pwsh -File Manuals/Scripts/set_visual_override_safe.ps1 -Port 5210 -ElementId <id>
```
2) 壁パラメータ更新の安全実行（コメント推奨）
```
pwsh -File Manuals/Scripts/update_wall_parameter_safe.ps1 -Port 5210 -ElementId <id> -Param Comments -Value "Test via smoke" -DryRun
pwsh -File Manuals/Scripts/update_wall_parameter_safe.ps1 -Port 5210 -ElementId <id> -Param Comments -Value "Test via smoke"
```
- 安全ポリシー: 先に `smoke_test` を実行し、OK の時だけ `__smoke_ok:true` で本命コマンドを送信。
- ゼロID禁止: `viewId: 0` / `elementId: 0` は絶対送信しない（スクリプトは自動で防止）。

## 4) ビュー操作のベストプラクティス（高信頼・非ブロッキング）
- 状態を壊さない可視化変更フロー:
  - 取得: `save_view_state` → 必要な可視化操作 → 復帰: `restore_view_state`
- テンプレート付きビューや重いビューでは更新版パラメータを活用:
  - `autoWorkingView:true` / `detachViewTemplate:true`
  - 分割実行: `batchSize`, `maxMillisPerTx`, `startIndex`, `refreshView`
  - ループ規約: レスポンスに `nextIndex` があれば、`startIndex=nextIndex` を付けて同メソッドを再実行（`completed:true` まで繰返し）
- 例（単要素に色/透明度）
```
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command set_visual_override --params "{\"elementId\":<id>,\"r\":0,\"g\":200,\"b\":255,\"transparency\":20,\"autoWorkingView\":true,\"__smoke_ok\":true}"
```

## 5) 代表コマンド（即戦力）
- 疎通: `ping_server`
- 環境: `agent_bootstrap`（アクティブビューID/単位など）
- レベル取得: `get_levels`（`-IdsOnly` 対応のPSスクリプトもあり）
- ビュー要素: `get_elements_in_view`（`idsOnly` 形状）
- 要素情報: `get_element_info`（`rich:true` で詳細）
- ビュー状態: `save_view_state` / `restore_view_state`
- 可視化: `set_visual_override`（更新版パラメータ推奨）
- 書出し: `export_dwg`（Viewer モードでは不可エラーあり）

## 6) 作業ルール（最短で迷わない）
- プロジェクト別に `Work/<ProjectName>_<ProjectID>` を作成し、その下で成果物・ログを管理。
- 参照系は `Manuals/`、編集や実験ログは `Work/` 配下へ。
- 書き込みは常に `*_safe.ps1` を最優先（`-DryRun` で事前確認）。
- 単位: 長さ=mm, 角度=deg（サーバー側で内部変換）。

リビジョン操作のまとめ: `Manuals/RevisionOps.md` を参照（一覧/シート関連/更新/クラウド作成）。
- レスポンス: JSON-RPC エンベロープ内の `result.result.*` を読む。
- 共通ステータス: `result.result.ok`, `result.result.code`, `result.result.msg`, `result.result.timings.totalMs`（詳細: `Manuals/Response_Envelope_JA.md`）。

## 7) トラブルシューティング（まずはここを確認）
- 500（enqueue）: リクエストに必ず `"params": {}` を含める／Revit がビジー → `/enqueue?force=1` 検討。
- 409: 他ジョブが実行中。完了待ち or `/enqueue?force=1`。
- ポーリングで無応答: Revit 側でモーダルダイアログ等が開いていないか確認。
- Viewer モード: `Exporting is not allowed` が出たら通常モードで再実行。
- ポート競合/確認:
```
Get-NetTCPConnection -LocalPort 5210,5211,5212 -State Listen | Select-Object LocalPort,OwningProcess,@{N='Name';E={(Get-Process -Id $_.OwningProcess).ProcessName}}
```

## 8) もっと進める（複数Revit・記録再生）
- 安定運用/多重インスタンス向けチェーン: Client → Proxy(5221) → Playbook(5209) → RevitMCP(5210+)
- ルーティング: `POST http://127.0.0.1:5221/t/{revitPort}/rpc`
- 詳細: `Manuals/ConnectionGuide/Revit_Connection_OneShot_Quickstart_EN.md`

## 9) 次に読むと良い原典
- 最短クイックスタート: `Manuals/ConnectionGuide/QUICKSTART.md`
- コマンド要点: `Manuals/Commands/HOT_COMMANDS.md`
- スクリプト一覧: `Manuals/Scripts/README.md`
- 可視化の更新版ガイド: `Manuals/UPDATED_VIEW_VISUALIZATION_COMMANDS_EN.md`

## 10) 実行ポリシー（Windows/PowerShell）重要
- 署名されていない `*.ps1` はブロックされる場合があります。実行時だけ Bypass を付けてください。
  - 例: `pwsh -ExecutionPolicy Bypass -File Codex/Manuals/Scripts/test_connection.ps1 -Port 5210`
  - 例: `pwsh -ExecutionPolicy Bypass -File Codex/Manuals/Scripts/export_walls_by_type_simple.ps1 -Port 5210`
- 詳細: `Manuals/ExecutionPolicy_Windows.md`

## 11) 高速DWG出力（seed + 壁タイプ別）
- ビューを直接いじらず、`export_dwg { elementIds: [...] }` で複製ビューに隔離して出力（高信頼）。
- 実行例（最新プロジェクト自動検出）:
  - `pwsh -ExecutionPolicy Bypass -File Codex/Manuals/Scripts/export_walls_by_type_simple.ps1 -Port 5210`

---
このドキュメントの手順通りに実施すれば、接続→取得→安全な書き込み→ビュー制御まで一通りカバーできます。うまくいかない場合は「7) トラブルシューティング」を参照し、必要に応じて原典ガイドを開いてください。

