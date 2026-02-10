# Update Log (Manual + Add-in)

## まとめ（現行版の主要更新ポイント）
> ここでは「現行アドインに存在する機能のみ」を、時系列ではなく**用途別に簡潔に整理**しています。

## 重要: 推奨ユーザーフォルダ構成の変更（Big Change）
- 旧: `Work/` 直下運用 → **新: `Projects/<ProjectName>_<ProjectID>/` 配下に統一**。
- 既定ルート: `%USERPROFILE%\\Documents\\Revit_MCP`（legacy: `Codex_MCP`）。
- 作業ログ/中間ファイル/スクリプト出力は **すべて `Projects/<...>` 配下**に集約。
- 詳細: `WORK_RULES.md` / `START_HERE.md` / `Manuals/README.md` を参照。

### 鉄筋（Rebar）
- AutoRebar（Plan → Apply）と RebarMapping による**属性ベース配筋**の整備（柱/梁の本数・ピッチ・径を反映）。
- 端部/中央の配筋区分、フック、被り厚さ、柱頭/柱脚区分などの**詳細挙動の強化**。
- レイアウト検査/更新・再生成・削除/移動など、**既存鉄筋の扱い**も拡張。

### Python Runner / スクリプト運用
- **プロジェクト単位フォルダ**への保存、Dedent、自動ポート引き渡し、Library/検索、MCPコマンド強調など、運用性を改善。
- CodexGUI → Python Runner への**安全なスクリプト受け渡し**を整備。

### 要素探索・空間要素の補正
- 要素検索/構造化クエリ、カテゴリ解決（曖昧語の候補提示）など**探索系を強化**。
- Room/Space/Area の取り違えを**自動補正**する仕組みを追加。

### ビュー・可視化・作図
- 色分け・線分描画・フィルタ適用などの**ビュー可視化系**を拡張。
- スケジュール/ビュー配置/タグなどの**実務支援コマンド**を整理。

### 安定性・ログ
- failureHandling / ダイアログ対応 / ロールバック時の**情報記録強化**。
- 実行ログやデバッグ情報の整理・安定化。

---

## 2026-02-04 Add-in: parallel safety + idempotency + stable ordering

### 目的
- 並行実行時の整合性を強化（docKey 安定化 / idempotency）。
- JSON の配列順序を安定化し、差分検証や比較を容易にする。

### 変更概要
- `context.docKey` を安定化（ledger docKey 優先、fallback は docPath / docTitle / UniqueId / central path を元にハッシュ）。
- `params.idempotencyKey` / `idemKey` による **短時間の結果キャッシュ**を追加。
- 要素配列の **安定ソート**（elementId/id/hostElementId 優先）。

### 詳細

## 2026-02-02 Codex GUI: session display + log restore + safe install

### 目的
- セッション切替時の出力表示を分かりやすくする（新規はクリア、既存は復元）。
- Codex セッションIDの表示/コピー機能を追加。
- CodexGUI の設定/ログがインストールで上書きされる問題を防止する。

### 変更概要
- 新規セッション選択時は出力をクリア、既存セッションへ戻るとログ末尾を復元表示。
- 「Codex Session ID」を表示・コピーできるダイアログを追加。
- Session ID 表示位置を Apply（BG/FG）の右側に配置（60pt 空け）。
- Codex GUI の保存先を `%LOCALAPPDATA%\\RevitMCP\\CodexGui\\` に移行（旧ファイルは自動移行）。
- `run_codex_prompt.ps1` で **存在しない profile を無視**して実行継続。
- 安全インストール用 `install_codexgui_safe.ps1` を追加（設定/ログを上書きしない）。

### 詳細

## 2026-01-28 Add-in + CodexGUI: Python script handoff (CodexGUI → Python Runner)

### 目的
- CodexGUI が生成する Python を **確実にファイル化**し、Python Runner から安全に開けるようにする。

### 変更概要
- CodexGUI: ` ```python ``` ` ブロックのみ保存対象（説明文やログを混在させない）。
- CodexGUI: 保存先を `Projects/<RevitFileName>_<docKey>/python_script/` に統一。
- CodexGUI: `# @feature:` / `# @keywords:` を自動付与（未指定は空欄）。
- Python Runner: CodexGUI 生成スクリプトは **Load Codex** から読み込み。
- Python Runner: 実行時に `REVIT_MCP_PORT` を自動設定（Python 側で参照可能）。
- Manual: `Manuals/ConnectionGuide/07_基本操作_PythonRunner_UI.md` を更新。

## 2026-01-26 Add-in: AutoRebar (Beam) start/end alignment + build fix

### 目的
- 梁（構造フレーム）の始端/終端の扱いを `LocationCurve.EndPoint(0/1)` に統一し、端部・中央の配筋区分やオプションが直感通りに動作するようにする。
- Manuals に記載済みのコマンドがビルドに入っていない等で「存在するのに使えない」状態を解消する。

### 変更概要
- AutoRebar（梁）: start/end を `LocationCurve.EndPoint(0/1)` に統一（スターラップだけでなく主筋等の start/end 系オプションにも適用）。
  - `Beam.Attr.Stirrup.PitchStart/Mid/End` がある場合、内法長さを `1/4 + 1/2 + 1/4` に分割し、`segment=start|mid|end` の3セットでスターラップを作成（best-effort）。
  - `hosts[].beamAxisStartEndByLocationCurve` / `hosts[].beamStirrupPitchZones` 等のデバッグ情報を追加。
- AutoRebar（柱）:
  - 柱の上下端について、コンクリート系の「構造柱/構造基礎」が接している場合は **軸方向の被りを 0** とみなし、主筋/帯筋が端部（ジョイント）まで届くように改善（best-effort）。
    - `MaterialClass` が取得できる場合はそれを優先し、`steel` / `metal` / `metallic` / `鋼` / `鉄骨` 系は除外。取得できない場合はマテリアル名の文字列（例: `コンクリート` / `Concrete` / `FC*` / `RC*`）から推定。
    - デバッグ: `hosts[].columnAxisEndConcreteNeighbor` / `hosts[].columnAxisEndCoverEffectiveMm` / `hosts[].columnAxisEndCoverEffectiveMm_ties`
    - 一部ファミリでローカル軸が反転している場合でも、`hosts[].axisPositiveIsUp` / `hosts[].axisDotZ` で world 上下を補正し、top/bottom の判定が逆転しないように修正。
  - 柱帯筋を中間高さで `base`（柱脚）+`head`（柱頭）に 2分割して plan を作成（既定ON）。片側の値が未指定/0 の場合は反対側を流用（仕様）。
    - オプション: `options.columnTieSplitByMidHeight` / `options.columnTiePitchBaseMm` / `options.columnTiePitchHeadMm`
    - mapping（任意）: `Column.Attr.Tie.PitchBaseMm` / `Column.Attr.Tie.PitchHeadMm`
  - `Column.Attr.Tie.PatternJson` の `reference.kind` に `column_mid` を追加（中間高さ基準のパターン指定が可能）。
- AutoRebar（梁）:
  - 2段目/3段目の主筋は、1段目の鉄筋位置にスナップして配置（best-effort）。本数=1 は左端、2本以上は左/右を必ず含み、3本目以降は 1段目位置に合わせます。
  - plan の主筋 action に `side` / `layerIndex` を付与（デバッグ/検証用）。
- Build: csproj に未登録でコンパイルされていなかったコマンドをビルド対象に追加し、関連するコンパイル不具合も修正。
  - `rename_floor_types_by_thickness` / `create_walls` / `delete_walls`
- Manual: `rebar_plan_auto`（EN/JA）に梁の start/end 定義を追記。
- Add-in: リボン（`RevitMCPServer` タブ）の配置を調整。
  - Python Runner を Codex GUI の左に配置（同じ `GUI` パネル内）。
  - Codex GUI のアイコンを “HAL” 風に変更（フォルダ風アイコンを廃止）。
  - Python Runner: Feature/Keywords を保存し、スクリプトの **Library**（一覧/検索/削除）を追加。

## 2026-01-23 Codex GUI: Prompt vertical resize + taskbar busy indicator

### 目的
- 入力欄（プロンプト）の高さを、作業内容に応じて手動で調整できるようにする。
- Codex GUI が背面に隠れていても「実行中/完了」が分かるようにする（タスクバーで視認）。

### 変更概要
- Codex GUI: プロンプト領域の上下リサイズ（ドラッグ）を追加（GridSplitter）。
- Codex GUI: 実行中はタスクバーのアイコンに赤いオーバーレイ表示＋進捗状態（Indeterminate）を表示。
- Codex GUI: 実行完了時にウィンドウが非アクティブなら、タスクバーアイコンを点滅（軽い通知）。

## 2026-01-23 Add-in: Python Runner MCP command highlight

### 目的
- Python Runner で、スクリプト中の MCP コマンド（JSON-RPC method 名）を見分けやすくする。

### 変更概要
- Add-in: `rpc("element.copy_elements", ...)` や `{"method":"doc.get_project_info", ...}` の **メソッド名**を濃い茶色＋ボールドで強調表示。

## 2026-01-22 Add-in: Python Runner UX + Client Guide

### 目的
- Python Runner の保存先をプロジェクト単位に統一し、ゴミ混入を防止する。
- 出力の視認性を改善し、結果だけをコピーしやすくする。
- MCP クライアント実装の注意点をまとめ、再発防止の指針を追加する。

### 変更概要
- Add-in: Python Runner の既定フォルダを `Projects/<RevitFileName>_<docKey>/python_script` に変更。
- Add-in: Save/Save As 前に dedent（共通先頭空白の削除）を適用。
- Add-in: 出力ウィンドウの時刻表示を「出力開始時のみ」に変更。
- Add-in: Python Runner リボンアイコンを「Py」バッジへ変更。
- Manual: `RevitMCP_Client_Dev_Guide.md` 追加。`START_HERE.md` / `AGENT_README.md` を更新。
- Manual: `family.query_loaded` の新規ドキュメント追加とコマンド索引の追記。

## 2026-01-21 Add-in: Dynamo graph execution (list/run)

### 目的
- Dynamo グラフを Revit 内で実行するための標準コマンドを提供する。
- スクリプトの配置場所を固定し、安全に実行できるようにする。

### 変更概要
- Add-in: `dynamo.list_scripts` / `dynamo.run_script` を追加。
- Add-in: `DynamoRunner` を追加（Dynamo 反射ロード、実行、出力取得）。
- Add-in: `dynamo.run_script` に `hardKillRevit` / `hardKillDelayMs` を追加（無人実行向け）。強制終了前にスナップショット/同期/保存/再起動を試行し、保存がブロックされる場合は UI スレッドでリトライ。強制終了前にサーバー停止と停止確認もログ化。
- Manual: Dynamo コマンドの EN/JA 手順書を追加し、README に追記。

## 2026-01-20 Add-in: Dialog auto-dismiss + dialog capture/OCR

### 目的
- 無人実行時にダイアログで停止しないようにする。
- ダイアログ内容をユーザーが確認できるように記録する。
- ダイアログのキャプチャと OCR を標準化する（best effort）。

### 変更概要
- Add-in: 全コマンドでダイアログを自動Dismiss（best effort）。
- Add-in: TaskDialog の詳細情報を優先取得（title/mainInstruction/expandedContent/footer）。
- Add-in: ダイアログ出現時に CaptureAgent を呼び出してキャプチャ＋OCRを試行。
- Manual: `failure_handling` / `capture.revit` の記述を更新。

## 2026-01-20 Add-in: Spatial bulk params + param suggestions

### 目的
- Room / Space / Area のパラメータを一括取得できるようにする。
- 曖昧なパラメータ名の解決を安全に補助する。
- ビュー内要素／カテゴリ取得の使い勝手と堅牢性を改善する。
- 曖昧なカテゴリ名を安全に解決できるようにする（IDベース運用の入口）。

### 変更概要
- 追加: `get_spatial_params_bulk`（Room / Space / Area の一括パラメータ取得、要素/パラメータのページング対応）。
- 追加: `spatial.suggest_params`（曖昧語から候補提示、fuzzy/contains/exact 対応）。
- 改善: `get_elements_in_view`
  - `viewId` 省略時はアクティブビューへフォールバック。
  - `categoryNames` は BuiltInCategory へ解決して事前フィルタ。
  - `count/skip` をトップレベルで受理。
  - `items` を `rows` のエイリアスとして返却。
  - カテゴリ絞り込みが 0 件の場合は未絞り込みで再試行。
- 改善: `get_categories_used_in_view`
  - `viewId` 省略時はアクティブビューへフォールバック。
  - `items` を `categories` のエイリアスとして返却。
- 追加: `meta.resolve_category`
  - `category_alias_ja.json` に基づき、曖昧なカテゴリ名を BuiltInCategory（OST_*）へ解決。
  - 文字化けの疑いがある入力をベストエフォートで回復。
  - 曖昧な場合は `ok=false` で候補提示。
- マニュアル: 新規コマンド追加と索引更新（EN/JA）。

## 2026-01-19 Codex GUI / Server: Capture stability + Model switching usability

### 目的
- Codex GUI の `Capture` が環境差（CaptureAgent 配置揺れ）で失敗/クラッシュする問題を解消する。
- Codex GUI の「モデル」欄を使いやすくし、空欄=デフォルトが確実に効くようにする。

### 変更概要
- Server: CaptureAgent 探索を `capture-agent\\` 優先 + `.dll` 同梱チェック（stale stub を回避）。
- Codex GUI:
  - Capture 失敗時に `code/exitCode/stderr` を表示。
  - 未処理例外を `%LOCALAPPDATA%\\RevitMCP\\logs\\codexgui.log` に記録（10MBでローテーション）。
  - ボーダーレス（`AllowsTransparency=True`）でも確実にリサイズできるよう、外周に透明リサイズハンドル（Thumb）を追加（左右/上下/四隅、8px）。
  - モデル欄を編集可能なドロップダウン＋`Default`/`Refresh` ボタンに変更（MRU+プリセット+`~/.codex/config.toml` 同期）。
  - `推論` 欄を追加し、`model_reasoning_effort`（low/medium/high/xhigh）をセッション単位で指定可能に。
  - `run_codex_prompt.ps1` は `-Model ""` / `-ReasoningEffort ""` を「前回維持」ではなく「明示クリア」に統一（既存セッションマップも hashtable に正規化して新キー追加で落ちない）。

## 2026-01-15 Add-in/Codex GUI: Element Query + Progress Display

### 目的
- 要素IDが不明な状態からでも、AI/ユーザーが安全に対象要素を絞り込めるようにする（探索/発見の強化）。
- 長時間処理の進捗を、ポップアップ無しで Codex GUI 上に見える形で表示する（Revit UI thread を汚さない）。

### 追加コマンド（Read）
- `element.search_elements`: キーワード検索（発見向け）
- `element.query_elements`: 構造化クエリ（カテゴリ/レベル/名前/bbox/パラメータ条件）

### 進捗表示（ファイル連携）
- Revit Add-in が `%LOCALAPPDATA%\\RevitMCP\\progress\\progress_<port>.jsonl`（JSONL）へ進捗スナップショットを書き出します（スロットリング）。
- Codex GUI がこれをポーリングして、進捗バー＋テキストを表示します（古い場合は自動で非表示）。

### 実装変更（主なファイル）
- `RevitMCPAddin/Core/CategoryResolver.cs`
- `RevitMCPAddin/Models/ElementQueryModels.cs`
- `RevitMCPAddin/Core/ElementQueryService.cs`
- `RevitMCPAddin/Commands/ElementOps/ElementQueryCommands.cs`
- `RevitMCPAddin/Core/Progress/ProgressState.cs`
- `RevitMCPAddin/Core/Progress/ProgressReporter.cs`
- `RevitMCPAddin/Core/Progress/ProgressHub.cs`
- `RevitMCPAddin/Core/CommandRouter.cs`
- `RevitMCPAddin/App.cs`
- `RevitMCPAddin/RevitMcpWorker.cs`
- `CodexGui/MainWindow.xaml`
- `CodexGui/MainWindow.xaml.cs`
- `Manuals/FullManual/element.search_elements.md`
- `Manuals/FullManual/element.query_elements.md`
- `Manuals/FullManual_ja/element.search_elements.md`
- `Manuals/FullManual_ja/element.query_elements.md`
- `Manuals/FullManual/README.md`
- `Manuals/FullManual_ja/README.md`
- `Manuals/README.md`

## 2026-01-15 Add-in: Spatial Selection Correction (Room / Space / Area)

### 目的
- Room/Space/Area の取り違えによるコマンド失敗を減らす（初心者がつまずきやすいポイントの吸収）。
- AI が「選択要素の種類を誤る」ケースでも、近接する適切な空間要素へ安全に補正できるようにする。

### 追加コマンド（Read）
- `element.resolve_spatial_selection`: 選択（または `elementId`）が Room/Space/Area の想定と違う場合に、近傍の目的の空間要素へ解決します。
  - 旧名 `resolve_spatial_selection` は alias（deprecated）です。

### 自動補正（ルータ）
- `get_room*` / `*_room` / `get_space*` / `*_space` / `get_area*` / `*_area` の系統コマンドでは、
  `roomId/spaceId/areaId/elementId` が取り違えられていても、**近傍の目的の空間要素**へ自動補正を試行します。

### 実装変更（主なファイル）
- `RevitMCPAddin/Core/SpatialElementResolver.cs`
- `RevitMCPAddin/Commands/Spatial/ResolveSpatialSelectionCommand.cs`
- `RevitMCPAddin/Core/CommandRouter.cs`
- `Manuals/FullManual/element.resolve_spatial_selection.md`
- `Manuals/FullManual_ja/element.resolve_spatial_selection.md`
- `Manuals/FullManual/README.md`
- `Manuals/FullManual_ja/README.md`

## 2026-01-15 Server: Screenshot Capture (CaptureAgent + capture.*)

### 目的
- Revit のエラー/ダイアログなどのスクリーンショットを **Revit 外部プロセス**で取得し、AI解析前に人間が確認できるようにする。
- Revit がビジーでも動作するように、キャプチャは **サーバー側のみ（no queue）**で完結させる。

### 追加コマンド（Read, server-local）
- `capture.list_windows`: トップレベルウィンドウ一覧（Win32 EnumWindows）
- `capture.window`: HWND 指定のウィンドウキャプチャ（PNG）
- `capture.screen`: モニターキャプチャ（PNG）
- `capture.revit`: Revit のダイアログ/メイン/フローティング等を一括キャプチャ（PNG）

### 出力とログ
- 既定の保存先（CaptureAgent）:
  - PNG: `%LOCALAPPDATA%\\RevitMCP\\captures\\`
  - JSONLログ: `%LOCALAPPDATA%\\RevitMCP\\logs\\capture.jsonl`
- `outDir` を指定すれば保存先を上書きできます。

### Codex GUI 同意ゲート（画像添付 `--image`）
- Codex GUI 上部の `Capture` ボタンで、`capture.*` による画像取得→プレビュー→承認/拒否（既定は拒否）までを一体で実行できます。
- 承認された画像のみが **次の 1 回の Codex 実行**に `--image` として添付されます。
- `risk: high`（図面/モデル表示が含まれる可能性が高い）を選択した場合は、追加の確認が必要です（既定は No）。

### 実装変更（主なファイル）
- `RevitMCPServer/Capture/CaptureService.cs`
- `RevitMCPServer/Capture/CaptureAgentRunner.cs`
- `RevitMCPServer/CaptureAgent/RevitMcp.CaptureAgent.csproj`
- `RevitMCPServer/CaptureAgent/Program.cs`
- `RevitMCPServer/Program.cs`
- `RevitMCPServer/Docs/CapabilitiesGenerator.cs`
- `RevitMCPServer/RevitMCPServer.csproj`
- `RevitMCPServer/RevitMCPServer.sln`
- `RevitMCPAddin/RevitMCPAddin.csproj`（server 同梱フォルダへ capture-agent もコピー）
- `CodexGui/CaptureConsentWindow.xaml(.cs)`
- `CodexGui/run_codex_prompt.ps1`（画像添付 `--image` 対応）
- `Manuals/FullManual/capture.*.md`
- `Manuals/FullManual_ja/capture.*.md`
- `Manuals/FullManual/README.md`
- `Manuals/FullManual_ja/README.md`
- `Manuals/Runbooks/Screenshot_Capture_Consent_CodexGui_*.md`

## 2026-01-14 Add-in: `help.suggest`（日本語グロッサリ＋決定論サジェスト）

### 目的
- コマンド数が増えた状態でも、ユーザー/エージェントが「日本語の曖昧な要求」から安全に最適なコマンドへ到達できるようにする（Discoverability）。
- LLM に依存せず、**決定論**で安定した候補提示（安全フラグ付き）を返す基盤を用意する。

### 変更概要
- 追加: `help.suggest`（Read）を追加し、日本語の問い合わせ文＋現在のRevitコンテキストから「レシピ/コマンド候補」を決定論的に提案できるようにしました。
  - `help.suggest` は **実行は行わず**、候補と推奨パラメータ（`proposedParams`）のみ返します。
  - `safeMode=true`（既定）では、明確な作成/変更意図が無い場合に Write 系を下げる挙動です。
- 追加: 日本語グロッサリ `glossary_ja.json` を同梱し、`help.suggest` がこれを利用します（見つからない場合は `glossary_ja.seed.json` をベストエフォートで探索）。
  - 上書き: 環境変数 `REVITMCP_GLOSSARY_JA_PATH`、または `%LOCALAPPDATA%\\RevitMCP\\glossary_ja.json`
  - 読み込み時に重複キーは `ja` フレーズをマージ、無効な `BuiltInCategory` は警告付きでドロップ（ベストエフォート）。

### 実装変更（主なファイル）
- `RevitMCPAddin/Core/GlossaryJaService.cs`
- `RevitMCPAddin/Commands/MetaOps/HelpSuggestHandler.cs`
- `RevitMCPAddin/glossary_ja.json`
- `Manuals/FullManual/help_suggest.md`
- `Manuals/FullManual_ja/help_suggest.md`

## 2026-01-14 Add-in: `view.draw_colored_line_segments`（座標データセット→色付き線分描画）

### 目的
- 「周長/外形/法線/解析結果」などの座標データセットを、**1回のMCP呼び出し**でビューへ描画できるようにし、エージェント側の多回呼び出し（線分ごとの `create_detail_line` 連打 + `set_visual_override`）を不要にする。

### 変更概要
- 追加: `view.draw_colored_line_segments`（Write）
  - `segments:[{start,end,...}]` または `loops:[{segments:[...]}]` を受け取り、ビューに詳細線分を作成します（入力はmm）。
  - 既定の線色/線太さ（`lineRgb` / `lineWeight`）に加え、セグメント単位で `lineRgb` / `lineWeight` の上書きも可能です。
  - 色/太さは **要素ごとのグラフィックス上書き**で適用します（線種自体は変更しません）。
  - ビューテンプレートが適用されているビューでは、上書きが効かないため `VIEW_TEMPLATE_LOCK` として中断（または `detachViewTemplate=true` で解除して実行）。

### 実装変更（主なファイル）
- `RevitMCPAddin/Commands/AnnotationOps/DrawColoredLineSegmentsCommand.cs`
- `RevitMCPAddin/RevitMcpWorker.cs`
- `RevitMCPAddin/RevitMCPAddin.csproj`
- `Manuals/FullManual/draw_colored_line_segments.md`
- `Manuals/FullManual_ja/draw_colored_line_segments.md`

## 2026-01-14 Server/Add-in/Codex GUI: Collaborative Chat (Phase 1)

### 目的
- Revit の作業共有チームで、人間が主役の「チャット（ログが正）」を導入し、AI を使えないメンバーも同じ記録を共有できるようにする。
- AIユーザーは、チャットの内容を `Copy→Codex` で Codex GUI へ貼り付け、必要なら安全な実行手順を **手動で**組み立てられるようにする。
- 招待（参加促し）を、Revit作業を妨げない通知で受け取れるようにする。

### 変更概要
- 追加（サーバー側・キュー不要）: `chat.post`, `chat.list`, `chat.inbox.list`
  - `RevitMCPServer` が直接処理し、DurableQueue に enqueue しません（Revit がビジーでも動作）。
  - 保存先は中央モデルフォルダ配下の `_RevitMCP\\projects\\<docKey>\\chat\\writers\\*.jsonl`（append-only）。
    - `docKey` は **プロジェクト固有の安定ID**（ViewWorkspace/Ledger の ProjectToken と同等）で、同一フォルダ内の複数 `.rvt` のログ混入を防ぎます。
  - ルート確定のため `docPathHint`（中央モデルのパス推奨）と `docKey`（推奨）が1回必要。Revit Add-in が ViewActivated で自動初期化します。
- 追加（Revit Add-in UI）: MCP Chat の DockablePane + リボンボタン（`RevitMCPServer`→`GUI`→`Chat`）
  - 招待は `ws://Project/Invites` へ `@userId` メンションで投稿。
  - 受信側は inbox をポーリングし、**非ブロッキングのトースト通知**で受け取ります。
- 追加（Codex GUI）: Chat Monitor
  - Chat Monitor は `chat.list` をポーリングしてメッセージを表示します。
  - `Copy→Codex` で選択メッセージ本文を Codex GUI の入力欄へ貼り付けられます（自動実行しません）。
  - コンプライアンスのため、チャットからの Codex 自動実行や `chat.post` による自動返信は行いません。

### 実装変更（主なファイル）
- `RevitMCPServer/Chat/ChatStore.cs`
- `RevitMCPServer/Chat/ChatRootState.cs`
- `RevitMCPServer/Program.cs`
- `RevitMCPAddin/UI/Chat/ChatDockablePane.cs`
- `RevitMCPAddin/Core/ChatRpcClient.cs`
- `RevitMCPAddin/Core/ChatInviteNotifier.cs`
- `RevitMCPAddin/Commands/Chat/ToggleChatPaneCommand.cs`
- `RevitMCPAddin/UI/RibbonPortUi.cs`
- `CodexGui/ChatMonitorWindow.xaml(.cs)`
- `Manuals/Chat/CollaborativeChatWorkflow_*.md`
- `START_HERE.md`, `Manuals/AGENT_README.md`

## 2026-01-15 Server/Add-in/Codex GUI: chat を docKey で分離

### 目的
- 同一フォルダ内に複数の `.rvt` がある環境で、チャットログが混ざる/上書きされる問題を防ぐ。
- Revit ホスト側の Json.NET バージョン差による `MissingMethodException` を回避する。

### 変更概要
- Chat ログの保存先を `docKey`（ViewWorkspace/Ledger の ProjectToken と同等）で分離しました。
  - 旧: `_RevitMCP\\chat\\writers\\*.jsonl`
  - 新: `_RevitMCP\\projects\\<docKey>\\chat\\writers\\*.jsonl`（`docKey` 不明時は `projects\\path-<hash>\\...`）
- Revit Add-in の Chat 呼び出しに `docKey` を付与し、サーバー側も `docKey` を受け取って保存先を決定します。
- Codex GUI の Chat Monitor が以下に対応しました。
  - `Copy→Codex` で選択メッセージ本文を Codex GUI の入力欄へ貼り付けられます（自動実行しません）。
  - `Copy` / `Copy All` でチャット内容をクリップボードへコピーできます。
  - コンプライアンスのため、チャットからの Codex 自動実行や `chat.post` による自動返信は行いません。
- Codex GUI の会話ログで、Codex CLI の “thinking/exec” などのトレース出力を別パネル（`Trace`）へ分離し、本文より目立たないようにしました。
  - `Trace` は既定で表示され、文字は小さめ・暗め（イタリック）です。
  - `Trace` は縦方向スプリッタで高さを手動調整できます（内容が増えても本文を勝手に押しつぶしません）。
  - `codex_core::codex: needs_follow_up` のような内部ログは、ユーザー向けの情報ではないため非表示にしました。
- Revit Add-in の MCP Chat ペインに `Copy` / `Copy All` を追加しました。
- Json.NET 互換ラッパーを更新し、`JToken.*` の欠落で落ちるケースを回避しました（JToken の新しめAPIを直接呼ばない）。
- Revit Add-in の MCP Chat ペインに `Auto`（自動更新）を追加しました。
  - ペイン表示中かつ `Auto=ON` の場合、約3秒ごとに `chat.list` を自動更新します（新着メッセージが押下なしで表示されます）。

### 実装変更（主なファイル）
- `RevitMCPServer/Chat/ChatRootState.cs`
- `RevitMCPServer/Chat/ChatStore.cs`
- `RevitMCPAddin/Core/JsonNetCompat.cs`
- `RevitMCPAddin/Core/ChatRpcClient.cs`
- `RevitMCPAddin/UI/Chat/ChatDockablePane.cs`
- `CodexGui/ChatMonitorWindow.xaml(.cs)`
- `CodexGui/MainWindow.xaml.cs`
- `Manuals/Chat/CollaborativeChatWorkflow_*.md`
- `START_HERE.md`, `Manuals/AGENT_README.md`

## 2026-01-13 Add-in: 部屋境界仕上げ壁（W5等）の柱追従を自動化

### 目的
- 部屋境界に沿った仕上げ壁作成（W5等）を、柱が Room Bounding でないモデルでも再現性高く実行できるようにする（重複作成・部屋間壁の混入を抑止）。

### 変更概要
- `room.apply_finish_wall_type_on_room_boundary` が、柱が通常 Room Bounding でないモデルでも柱セグメントを拾えるように改善しました。
  - 既定で `autoDetectColumnsInRoom=true` + `tempEnableRoomBoundingOnColumns=true` とし、候補柱の `Room Bounding` を **一時的にON→境界取得→元に戻す**（仕上げ壁作成のみ残る）方式です。
  - 応答に `autoDetectedColumnIds` / `toggledColumnIds` を追加（デバッグ用）。
- 連続する境界セグメント同士が角でつながるよう、基準線をコーナートリムしてから壁を作成できるようにしました（既定 `cornerTrim=true`）。
- 追加: `excludeWallsBetweenRooms=true` を指定すると、壁セグメントの反対側に別Roomがある場合はそのセグメントを除外できます（部屋間の壁をスキップ）。
  - 判定距離は `adjacencyProbeDistancesMm`（既定 `[250,500,750]` mm、細い部屋での取りこぼし抑制）。
  - 判定ロジックは `get_candidate_exterior_walls` と同等の「壁法線 ± 複数距離プローブ + Room.IsPointInRoom（各Roomの基準レベルZ）」です。
  - 追加: `restrictBoundaryColumnsToEligibleWalls=true` で、上記フィルタ後の「有効な壁」に隣接する柱だけを対象にできます。
- `skipExisting=true` の既存仕上げ壁検出を修正し、再実行時の重複作成を抑止しました（既存壁照合を「境界曲線」ではなく「作成に使う基準線（境界＋厚さオフセット）」に統一）。
- `set_visual_override` / `batch_set_visual_override` で、線色と塗りつぶし色を別指定できるようにしました（`lineRgb` / `fillRgb`）。

### 実装変更（主なファイル）
- `RevitMCPAddin/Commands/Room/ApplyFinishWallsOnRoomBoundaryCommand.cs`
- `RevitMCPAddin/Commands/VisualizationOps/SetVisualOverrideCommand.cs`
- `RevitMCPAddin/Commands/VisualizationOps/BatchSetVisualOverrideCommand.cs`
- `Manuals/FullManual/apply_finish_wall_type_on_room_boundary.md`
- `Manuals/FullManual_ja/apply_finish_wall_type_on_room_boundary.md`

## 2026-01-13 Add-in: View Filter（V/G → Filters）のCRUD + 適用をコマンド化

### 目的
- ビューのフィルタ（V/G → Filters）操作をコマンド化し、カテゴリ表示/色分け等をバルクで自動化できるようにする。

### 変更概要
- View Filter 管理コマンドを追加しました（ParameterFilterElement / SelectionFilterElement）。
  - `view_filter.list` / `view_filter.get_order`（Read）
  - `view_filter.upsert` / `view_filter.delete` / `view_filter.apply_to_view` / `view_filter.remove_from_view` / `view_filter.set_order`（Write）
- ルールは MVP として `logic:"and"` を実装（ORは将来拡張）。
- パラメータ指定は `builtInParameter` / `sharedParameterGuid` を推奨し、`parameterName` は「categories の共通フィルタ可能パラメータ」に限定して best-effort 解決します（曖昧/未解決は失敗）。
- ビューテンプレートが適用されているビューはロックされることがあるため、`detachViewTemplate=true`（またはテンプレートビューを直接指定）に対応しました。

### 実装変更（主なファイル）
- `RevitMCPAddin/Commands/ViewFilterOps/ViewFilterCommands.cs`
- `Manuals/FullManual/view_filter.list.md`
- `Manuals/FullManual/view_filter.upsert.md`
- `Manuals/FullManual_ja/view_filter.list.md`
- `Manuals/FullManual_ja/view_filter.upsert.md`

## 2026-01-09 Server: revit.status の “ghost RUNNING” 回収 + docs系エンドポイントの canonical-only 既定化

### 目的
- `revit.status` のキュー統計で `RUNNING/DISPATCHING` が残留して誤認される問題を解消し、停止/クラッシュ後も自動回収できるようにする。
- docs/capabilities を canonical-only 既定に統一し、エージェントが迷わず legacy→canonical を解決できるようにする。

### 変更概要
- Add-in: `element.create_flush_walls`（alias: `create_flush_walls`）を追加し、既存壁に密着（面合わせ）する壁を別タイプで作成できるようにしました（既定は仕上面・ByGlobalDirection）。
  - 既定は上下拘束（Base/Top/Offset/Height）の複製まで（Attach Top/Base の完全複製は Revit 2023 API では困難なため未対応）。
- Add-in: `room.apply_finish_wall_type_on_room_boundary`（alias: `apply_finish_wall_type_on_room_boundary`）を追加しました（部屋境界セグメント長で仕上げ壁を作成、端部結合を Allow に設定）。
  - 追記: `includeBoundaryColumns=true`（既定）で、境界要素が柱（構造柱/建築柱）のセグメントも対象にできます（柱が Room Bounding として境界に出ている場合）。
- `revit.status` が極端に古い `RUNNING/DISPATCHING` を best-effort で回収（`DEAD` / `error_code:"STALE"`）し、`RUNNING` が残留して誤認される問題を解消しました。
  - 既定しきい値: 6時間（`REVIT_MCP_STALE_INPROGRESS_SEC` で変更可）
  - 応答に `staleCleanup`（`staleAfterSec`, `reclaimedCount`）を追加
- サーバー起動後も 10分ごとに stale 回収を走らせるようにし、`revit.status` を呼ばない運用でも回収されるようにしました（best-effort）。
- `GET /docs/manifest.json` の既定を canonical-only（deprecated alias 除外）に変更。
  - deprecated も含める場合: `GET /docs/manifest.json?includeDeprecated=1`
- `GET /debug/capabilities` の既定を canonical-only（deprecated alias 除外）に変更。
  - deprecated も含める場合: `GET /debug/capabilities?includeDeprecated=1`
  - alias→canonical を一覧で確認する場合: `GET /debug/capabilities?includeDeprecated=1&grouped=1`

### 実装変更（主なファイル）
- `RevitMCPServer/Program.cs`
- `RevitMCPServer/Engine/DurableQueue.cs`
- `RevitMCPServer/Docs/CapabilitiesGenerator.cs`
- `RevitMCPAddin/Commands/ElementOps/Wall/CreateFlushWallsCommand.cs`
- `RevitMCPAddin/Commands/Room/ApplyFinishWallsOnRoomBoundaryCommand.cs`

## 2026-01-08 Docs: capabilities（機械可読なコマンド一覧）を追加

### 目的
- サーバー側で「コマンド実装状況（capabilities）」を機械可読に提供し、エージェントが安全に必要パラメータを確定できるようにする。

### 変更概要
- サーバーに `GET /debug/capabilities` を追加し、現在把握しているコマンド実装状況（最後に受信したマニフェスト由来）を JSON で返すようにしました。
- サーバー側で `docs/capabilities.jsonl`（1行=1コマンド）を自動生成するようにしました（マニフェスト読み込み/登録時、best-effort）。
- 修正: `since` が混在しないよう、サーバー注入の `revit.status` / `status` / `revit_status` は「最後に受信したマニフェスト内でもっとも多い `since`」に合わせます（マニフェストが空の場合のみサーバー側 `since` にフォールバック）。
- 注意:
  - これらは「最後に受信したマニフェスト」を反映します。Revit 起動後にアドインが同ポートのサーバーへ接続してマニフェスト送信していることが前提です。
  - capabilities の各フィールドはスキーマ安定です（値が取得できない場合は安全な既定値で補完されます）。
  - 追加: alias 解決を確実にするため `canonical` フィールドを追加しました（deprecated alias の正規名）。

### 実装変更（主なファイル）
- `RevitMCPServer/Docs/CapabilitiesGenerator.cs`
- `RevitMCPServer/Docs/DocModels.cs`
- `RevitMCPServer/Docs/ManifestRegistry.cs`

## 2026-01-08 Step 4: canonical/alias（正規名/従来名）ポリシーを強化

### 目的
- “有効コマンド（canonical）” を namespaced 名に統一し、legacy 名は deprecated alias として残すことで混乱を抑止する。

### 変更概要
- canonical（正規名）は **namespaced**（`*.*`）を基本とし、従来名は alias として残しつつ `deprecated=true` 扱いに統一。
- `list_commands` / `search_commands` は **canonicalのみを返す**のがデフォルトになりました（`includeDeprecated=true` で deprecated を含める）。
- capabilities では `summary`/`resultExample`/`supportsFamilyKinds`/`since`/`revitHandler` などを欠損させず、機械可読性を優先して自動補完します（best-effort）。
- capabilities の legacy 名整理:
  - `revit.status` を正規名に統一し、`status` / `revit_status` は deprecated alias 扱いに変更。
  - `test_cap` はテスト混入のため除外（出力されません）。
  - `/manifest/register` は同一 `source` の登録を上書き扱いに変更し、古いコマンドが残り続ける問題（stale）を回避。

### 実装変更（主なファイル）
- `RevitMCPServer/Docs/ManifestRegistry.cs`
- `RevitMCPServer/Docs/CapabilitiesGenerator.cs`
- `RevitMCPAddin/Core/CommandNaming.cs`

## 2026-01-08 ViewOps: 3Dビュー作成コマンドの重複整理 + sheet.list を Read 扱いに修正

### 目的
- 同機能の重複コマンドを整理してエージェントの迷いを減らし、Read系コマンドで不要な Write/Undo を発生させない。

### 変更概要
- `view.create_focus_3d_view_from_selection` を正として統一し、重複して見えていた `view.create_clipping_3d_view_from_selection` は deprecated alias 扱いに整理（エージェントが迷わないように）。
- deprecated alias の `summary` を `deprecated alias of <canonical>` 形式に変更（capabilities上で混乱しないように）。
- `sheet.list`（および alias の `get_sheets`）が kind/transaction を誤って `Write` と推定していたため、推定ロジックを修正して `Read` に統一。
- “末尾一致で辿れない legacy→canonical 変換” は `LegacyToCanonicalOverrides`（明示aliasMap）で解決するように追加（例: `place_view_on_sheet_auto` / `sheet_inspect` / `revit_batch` / `revit_status`）。

### 実装変更（主なファイル）
- `RevitMCPAddin/Commands/ViewOps/CreateFocus3DViewFromSelectionCommand.cs`
- `RevitMCPAddin/Core/CommandNaming.cs`
- `RevitMCPServer/Docs/CapabilitiesGenerator.cs`

## 2026-01-07 AutoRebar: RUG柱（RC_C_B:1C1）向けプロファイル追加 + RebarBarTypeの「径フォールバック」解決

### 目的
- RUGファミリ（例: 構造柱 `RC_C_B : 1C1`）で、`D22` 等の **鉄筋タイプ名がプロジェクトに存在しない**場合でも配筋できるようにする。
- ファミリ固有パラメータは **ソースコードにハードコードせず**、`RebarMapping.json` のプロファイルで吸収する。

### 症状
- `rebar_plan_auto` / `rebar_apply_plan` / `rebar_regenerate_delete_recreate` 実行時に、`Main bar type not found: D22` 等で失敗する。
  - プロジェクト側に `RebarBarType` 名が `D22` で存在しないケースがある（命名規則依存）。

### 変更概要
- 鉄筋タイプ解決を堅牢化:
  - まず `RebarBarType` 名の完全一致で探索（従来どおり）。
  - 見つからない場合、指定文字列に `D25` のような径情報が含まれていれば、`RebarBarType.BarModelDiameter` から **径一致で探索**して解決（命名規則に依存しない）。
  - 解決の詳細を `mainBarTypeResolve` / `tieBarTypeResolve` としてホスト別に返す（requested/resolved/resolvedBy/diameterMm）。
- `RebarMapping.json` に RUG 柱のプロファイルを追加:
  - `rug_column_attr_v1`（priority 110）
  - `D_main` / `D_hoop` / `pitch_hoop_panel` / `N_main_*` 等のタイプパラメータから、柱の主筋/帯筋の入力を吸収（best-effort）。
- `RebarMapping.json` に RUG 梁（構造フレーム）のプロファイルを追加:
  - `rug_beam_attr_v1`（priority 120）
  - `D_main` / `D_stirrup` / `N_main_top_total` / `N_main_bottom_total` / `pitch_stirrup*` 等のタイプパラメータから、梁の主筋本数とスターラップピッチを吸収（best-effort）。
  - 2段/3段筋がある場合は `N_main_*_1st/_2nd/_3rd` を `Beam.Attr.MainBar.*Count2/*Count3` にマップし、断面内で層をスタックして作成（簡易近似、`options.beamMainBarLayerClearMm` で層間クリア変更可）。

### 2026-01-07 AutoRebar: 鉄筋中心間離隔テーブルの導入（段筋層ピッチ + plan-time チェック）
- `RebarBarClearanceTable.json` を追加し、鉄筋径→中心間離隔（mm）をファイル化。
- 梁の2段/3段筋の層ピッチ（中心-中心）はこのテーブルを優先して使用（無い径のみ `barDiameter + options.beamMainBarLayerClearMm` にフォールバック）。
- `rebar_plan_auto` 応答の `beamMainBarClearanceCheck` で、計画上の最小間隔（層内・層間）がテーブル値を満たすかを返す（作成前の検証用）。

### 2026-01-07 AutoRebar: 作成済みRebarの実測間隔チェック（read）
- `rebar_spacing_check` を追加し、モデル上のRebar中心線から実測の中心間距離を算出して離隔チェックします。
- `RebarBarClearanceTable.json` の径→中心間(mm) を基準に判定し、必要なら違反ペアを返します（`includePairs=true`）。

### 実装変更（主なファイル）
- `RevitMCPAddin/Core/RebarAutoModelService.cs`
- `RevitMCPAddin/RebarMapping.json`
- `RevitMCPAddin/RebarBarClearanceTable.json`
- `RevitMCPAddin/Commands/Rebar/RebarSpacingCheckCommand.cs`

## 2026-01-07 UI: Undo履歴の表示名をコマンド別に改善

### 目的
- Undo履歴で「どのMCPコマンドを実行したか」が判別しやすいようにする。

### 変更概要
- Undoスタックに表示される `MCP Ledger Command` を、`MCP <短いラベル>`（コマンド名から自動生成）に変更。

### 実装変更（主なファイル）
- `RevitMCPAddin/Core/McpLedger.cs`

## 2026-01-07 Maintenance: ローカルキャッシュ自動クリーンアップ + 手動コマンド追加

### 目的
- `%LOCALAPPDATA%\\RevitMCP` 配下の古いキャッシュ（マニフェスト/一時ファイル等）の残留による混乱と肥大化を抑止する。

### 変更概要
- Revit 起動時に `%LOCALAPPDATA%\\RevitMCP` 配下の「7日より古いキャッシュ」を best-effort で自動削除するようにしました（現行ポート/稼働中ポート推定は除外）。
- 手動実行用に `cleanup_revitmcp_cache`（dryRun 既定）を追加しました。
- `RebarMapping.json` / `RebarBarClearanceTable.json` の探索順を「アドインフォルダ優先」に変更しました（既定は同梱ファイルを使用、`%LOCALAPPDATA%\\RevitMCP` は上書き/キャッシュ扱い）。

### 実装変更（主なファイル）
- `RevitMCPAddin/App.cs`
- `RevitMCPAddin/Core/CacheCleanupService.cs`
- `RevitMCPAddin/Commands/System/CleanupRevitMcpCacheCommand.cs`
- `RevitMCPAddin/Core/RebarMappingService.cs`
- `RevitMCPAddin/Core/Rebar/RebarBarClearanceTableService.cs`
- `RevitMCPAddin/RebarMapping.json`
- `RevitMCPAddin/RebarBarClearanceTable.json`
- `Manuals/FullManual/cleanup_revitmcp_cache.md`
- `Manuals/FullManual_ja/cleanup_revitmcp_cache.md`

## 2025-12-26 ? AutoRebar: 柱の主筋（各面本数）+ 梁上面基準の帯筋パターン（上3@100 / 下2@150）

### 目的
- 柱主筋を「四隅のみ」ではなく、**各面本数（例: 各面5本）**で計画/作成できるようにする。
- 柱帯筋について、梁接合部付近の密度を簡易に表現できるよう、**梁上面（参照面）を基準にした上下別ピッチ・本数**のパターンを追加する。

### 変更概要
- `rebar_plan_auto` の柱主筋:
  - mapping `Column.Attr.MainBar.BarsPerFace` と `options.columnMainBarsPerFace`（>=2）で「各面本数」を指定可能。
  - 2 の場合は従来どおり四隅のみ、5 の場合は矩形柱で 16 本（例）。
- `rebar_plan_auto` の柱帯筋:
  - まず mapping `Column.Attr.Tie.PatternJson`（推奨）を読み取り、任意の「参照面 + セグメント（方向/本数/ピッチ）」で帯筋セットを生成（best-effort）。
    - 参照面 `beam_top` / `beam_bottom` は「梁せい（mapping `Host.Section.Height`）+ LocationCurve Z（=レベル/オフセット由来）」から上下面Zを推定（best-effort、必要に応じて bbox で補助）。
    - 生成される role は `column_ties_pattern_*`。
  - 互換のため `options.columnTieJointPatternEnabled=true` も残し、内部的に `columnTiePattern` に変換して同等に処理。
  - さらに、同一ホスト内の「タグ付き鉄筋」でユーザーがフック設定（`始端のフック/終端のフック/回転`）を調整している場合は、それを次回の自動作成に引き継ぐ（`options.hookAutoDetectFromExistingTaggedRebar=true`、既定、best-effort）。

### 実装変更（主なファイル）
- `RevitMCPAddin/Core/RebarAutoModelService.cs`
- `Manuals/FullManual/rebar_plan_auto.md`
- `Manuals/FullManual_ja/rebar_plan_auto.md`

## 2025-12-25 — Auto Rebar v1（柱=主筋+帯筋 / 梁=主筋+スターラップ）

### 目的
- 既存の柱/梁（ホスト）に対して、**簡易ルールで鉄筋を自動モデル化**できる土台を追加（検証・下地作り）。

### 追加コマンド
- `rebar_plan_auto`（read）: 選択/指定ホストから **作成計画(plan)** を生成（モデル変更なし）
- `rebar_apply_plan`（write）: plan を適用して **Rebar要素を作成**（plan省略時は plan生成→適用を一括）

### 挙動/仕様（v1）
- 対象カテゴリ: `OST_StructuralColumns` / `OST_StructuralFraming` のみ
- 柱: 主筋4本（四隅）+ 帯筋1セット
- 梁: 主筋（上/下）+ スターラップ1セット
  - 既定の主筋本数: 上=2、下=2（`options.beamMainTopCount/beamMainBottomCount` または mapping 梁属性キーで上書き）
- 形状は近似（厳密な配筋設計用途ではなく、作業支援/検証用のスキャフォールド）
  - 梁の長手方向は Solid ジオメトリ由来の物理形状範囲を優先（取れない場合は BoundingBox にフォールバック。LocationCurve 端点は柱芯まで伸びるケースがあるため）
- 梁の主筋は、可能であれば「支持柱の幅（梁軸方向）」から延長長さを推定し、**柱内へ 0.75×柱幅** だけ伸ばします（best-effort）。
  - 推定に失敗する場合は柱内延長はスキップします（`options.beamMainBarEmbedIntoSupportColumns=false` で無効化可能）。
- 梁スターラップは、可能であれば **構造柱のBoundingBox** から「柱面（支持面）」を推定し、スターラップの開始/終了をそこに寄せます（best-effort）。
  - まず結合（Join）された柱を試し、無い場合は梁端点近傍の柱を探索します。
- 梁スターラップはフックも指定可能（best-effort）。`options.beamStirrupUseHooks=true` かつ `beamStirrupHookAngleDeg=135` 等を指定すると、開いた折れ線＋両端フックとして作成します。
- 柱の主筋: 端部延長/短縮・折り曲げ（線分表現）を options で指定可能。
- 柱の帯筋（せん断補強筋）: 開始/終了位置オフセット、135度フック（両端）と向きを options で指定可能（best-effort）。
- 作成したRebarには `Comments = RevitMcp:AutoRebar` を付与（後から `rebar_layout_update_by_host` の filter で一括調整しやすくするため）
- `rebar_apply_plan` は `deleteExistingTaggedInHosts=true` を指定すると、各ホスト内の同一タグ鉄筋を削除してから再作成できます（既存自動配筋の更新用）。
- 失敗時の影響範囲を最小化するため、**ホスト単位でトランザクション分離**（失敗ホストのみロールバック、他は継続）
- `Rebar.CreateFromCurves` がフック未指定で失敗する場合、利用可能なフックタイプを探索して再試行（best effort）
- 梁について、`RebarMapping.json` のプロファイルに梁用の論理キー（`Beam.Attr.*`）が定義されている場合はそれを優先可能（best effort）
  - 例: `Beam.Attr.MainBar.DiameterMm`, `Beam.Attr.MainBar.TopCount`, `Beam.Attr.Stirrup.DiameterMm`, `Beam.Attr.Stirrup.PitchMidMm` 等
  - `options.beamUseTypeParams`（既定: true）
  - 鉄筋タイプは「径から一致する `RebarBarType` を探索」して選択（命名に依存しない）
  - ピッチが全て 0 の場合は「スターラップ無し」と解釈してスキップ
  - パラメータ名はファミリ作成者/言語で変わるため、パラメータ名は `RebarMapping.json` 側に登録（例: `rc_beam_attr_jp_v1`）

### 実装変更（主なファイル）
- `RevitMCPAddin/Core/RebarAutoModelService.cs`
- `RevitMCPAddin/Commands/Rebar/RebarPlanAutoCommand.cs`
- `RevitMCPAddin/Commands/Rebar/RebarApplyPlanCommand.cs`
- `RevitMCPAddin/RevitMcpWorker.cs`
- `RevitMCPAddin/RevitMCPAddin.csproj`
- `Manuals/FullManual/rebar_plan_auto.md`
- `Manuals/FullManual/rebar_apply_plan.md`
- `Manuals/FullManual_ja/rebar_plan_auto.md`
- `Manuals/FullManual_ja/rebar_apply_plan.md`
- `Manuals/Commands/commands_index.json`
- `Manuals/Commands/Commands_Index.all.en.md`
- `Manuals/Commands/revitmcp_commands_full.jsonl`
- `Manuals/Commands/revitmcp_commands_extended.jsonl`

### テスト方法（例）
- `get_selected_element_ids` で、柱+梁が選択されていることを確認
- `rebar_apply_plan` を `dryRun:true` で実行 → `dryRun:false` で作成

## 2025-12-24 — Rollback時の警告詳細を必ず記録（failureHandling/Router強化）

### 目的
- ロールバック（`TX_NOT_COMMITTED` など）発生時に、警告ダイアログが閉じられるまで次の作業ができない問題があり、原因追跡のための **警告詳細の確実な記録** が必要。

### 変更概要（挙動）
- ルータがコマンド実行中の failure/dialog を **常時 capture**（`failureHandling` 未指定でも）するように変更。
  - `failureHandling` がオフの場合は **capture-only** で、警告削除/解決/ロールバックなどの介入は行いません。
- コマンド結果がロールバック相当（例: `code: "TX_NOT_COMMITTED"`）の場合、`failureHandling` を明示していなくても **`failureHandling.issues` を応答に自動付与**し、警告詳細が必ず残るように変更。
- `failureHandling` 有効時は、モーダルダイアログがキューをブロックしないよう **自動Dismiss（best effort）** を行い、Dismissした事実を `failureHandling.issues.dialogs[]` に記録。

### 追加された応答フィールド（代表）
- `failureHandling.issues`（rollback時に自動付与されることがある）
- `failureHandling.rollbackDetected: true`
- `failureHandling.rollbackReason`
- `failureHandling.autoCaptured: true`（暗黙付与された場合）
- `failureHandling.issues.dialogs[].dismissed`, `failureHandling.issues.dialogs[].overrideResult`

### 実装変更（主なファイル）
- `RevitMCPAddin/Core/CommandRouter.cs`（rollback検出時の issues 付与、常時capture）
- `RevitMCPAddin/Core/Failures/FailureHandlingScope.cs`（capture-only対応、rollback情報の保持、ダイアログDismiss記録）
- `RevitMCPAddin/Core/Failures/FailureHandlingFailuresPreprocessor.cs`（rollback情報の保持）
- `RevitMCPAddin/Core/Failures/FailureRecord.cs`（issuesの拡張: rollbackRequested/rollbackReason 等）
- `Design/failure_whitelist.json`（`DuplicateInstances` の参照修正）
- `Manuals/FullManual/failure_handling.md`
- `Manuals/FullManual_ja/failure_handling.md`
- `Manuals/Response_Envelope_EN.md`
- `Manuals/Response_Envelope_JA.md`

### テスト方法（例）
- `Scripts/Reference/test_failure_handling_overlapping_wall.ps1`（壁重なり警告の再現）
- 既存部屋に重ねて `create_room` 実行（Roomの重複警告→rollbackの再現）

### 互換性・注意
- 既存の応答形式に対して **additive（追記）** のみで、既存クライアントを壊さない方針。
- ダイアログDismissは best effort です（Revit側ダイアログ仕様に依存）。

## 2025-12-24 — ViewWorkspace: 初回スナップショット未存在時のスパム/早期断念を改善

### 目的
- 初回（スナップショット未作成）でもログスパムにならないよう抑止し、次回復元できるベースラインスナップショットを確実に残す。

### 症状
- Revitでプロジェクトを開いた直後にログへ:
  - `auto-restore snapshot not found`
  - `auto-restore gave up (too many attempts)`
  が短時間に大量出力される。

### 原因
- `Idling` が高頻度で発火する環境では、attemptカウントが瞬時に消費されるため。
- 初回（スナップショット未作成）でも「復元を試行→失敗」を繰り返していたため。

### 対応
- スナップショットファイルが存在しない場合はリトライしない（スパム抑止）。
- 初回は自動restoreをスキップし、次回復元できるよう **ベースラインスナップショットを自動保存**。
- attempt 消費が一瞬で尽きないよう、restore試行を **時間でスロットリング**（best effort）。

### 実装変更（主なファイル）
- `RevitMCPAddin/Core/ViewWorkspace/ViewWorkspaceService.cs`
- `Manuals/FullManual/restore_view_workspace.md`
- `Manuals/FullManual_ja/restore_view_workspace.md`

## 2025-12-24 — get_room_finish_takeoff_context 追加（部屋仕上げの前処理データ取得）

### 目的
- 部屋の仕上げ数量算出（壁面積、柱周り、開口控除など）のために、境界線/近傍壁/柱/開口を**単一コマンドでまとめて**取得できるようにする。

### 変更概要（挙動）
- `get_room_finish_takeoff_context` を追加。
  - 部屋境界（座標＋長さ＋境界要素ID/種別）を返却。
  - 境界セグメントに近接する壁を 2D 幾何でマッチングし、`loopIndex/segmentIndex` との対応を返却。
  - マッチした壁がホストするドア/窓（挿入要素）を壁ごとに整理して返却。
  - 柱（構造柱/建築柱）の取得（指定 or 自動検出）と、柱↔壁の近接（BoundingBox近似）を返却。
  - 必要に応じて柱の Room Bounding を一時的に ON にして境界を計算（TransactionGroup rollback でモデルは不変更）。

### 注意（現時点）
- `metrics.estWallAreaToCeilingM2` は `wallPerimeterMm × roomHeightMm` の概算（開口控除なし）。
- 壁マッチング/柱近接は幾何近似（閾値はパラメータで調整可能）。
- 周長の比較用に、Revitの周長パラメータ値（`metrics.perimeterParamMm` / `metrics.perimeterParamDisplay`）も返します。
- 仕上げ数量の前処理用の参考として、床/天井の関連要素（`floors[]` / `ceilings[]`）と、セグメント単位の天井高さサンプル（`loops[].segments[].ceilingHeightsFromRoomLevelMm`）を返します（bbox＋室内サンプル点の近似）。
- 部屋内（交差含む）の要素を収集する `interiorElements` を追加（既定は `includeInteriorElements=false`。`interiorCategories` 未指定時は「積算向けオブジェクトカテゴリ」（壁/床/天井/屋根/柱/建具/家具/設備など）を対象。`insideLengthMm` は `LocationCurve` をサンプリングして部屋内長さを概算）。点ベース要素は既定で `interiorElementPointBboxProbe=true` とし、基準点が室外でもBBoxが室内に掛かるケースを拾う（出力は `measure.referencePointInside` / `measure.bboxProbe.used` で判別）。
- 比較JSON（`test_room_finish_takeoff_context_compare_*.json`）から標準化した `SegmentWallTypes` シートを再生成するスクリプトを追加:
  - `Scripts/Reference/room_finish_compare_to_excel.py`

### 実装変更（主なファイル）
- `RevitMCPAddin/Commands/Room/GetRoomFinishTakeoffContextCommand.cs`
- `RevitMCPAddin/RevitMcpWorker.cs`
- `Manuals/FullManual/get_room_finish_takeoff_context.md`
- `Manuals/FullManual_ja/get_room_finish_takeoff_context.md`

## 2025-12-25 ? get_room_finish_takeoff_context: 床/天井のレベル混入を抑止（既定で同一レベルのみ）

### 目的
- `get_room_finish_takeoff_context` の床/天井取得で別レベルの要素が混入する問題を抑止し、結果の信頼性を上げる（既定で同一レベルのみ）。

### 症状
- `includeFloorCeilingInfo` の床/天井収集は、境界線の室内サンプル点に対して「BBoxのXY一致」で候補を拾うため、建物が上下に積層していると **別レベルの床/天井が混入**することがある。

### 対応
- 既定で、床/天井候補を **部屋の `levelId` と同一レベル**の要素のみにフィルタ。
  - 新パラメータ: `floorCeilingSameLevelOnly`（既定 `true`）
- `floors[]` / `ceilings[]` に `levelId` / `levelName` を付与（追跡しやすくするため）。

### 実装変更（主なファイル）
- `RevitMCPAddin/Commands/Room/GetRoomFinishTakeoffContextCommand.cs`
- `Manuals/FullManual/get_room_finish_takeoff_context.md`
- `Manuals/FullManual_ja/get_room_finish_takeoff_context.md`
- `Manuals/Commands/revitmcp_commands_extended.jsonl`
- `Manuals/Commands/revitmcp_commands_full.jsonl`

## 2025-12-25 — RebarMapping / Rebar layout コマンド追加

### 目的
- ファミリ/言語差を吸収する配筋パラメータマッピング基盤と、既存レイアウト鉄筋の検査/更新コマンドを整備する。

### 変更概要
- `RevitMCPAddin/RebarMapping.json` を追加（論理キー → instance/type/built-in/derived/constant のマッピング）。
- プロファイル選択を強化（`priority`, `familyNameContains`, `typeNameContains`, `requiresTypeParamsAny`, `requiresInstanceParamsAny`）。
- `sources[].kind` に `instanceParamGuid` / `typeParamGuid` を追加（共有パラメータGUIDで言語差を吸収）。
- `double/int` の読み取りで、Spec が `Length` のときだけ ft→mm 変換するよう修正（RC系ファミリで mm を数値として持つケース対応）。
- 梁の配筋属性キー `Beam.Attr.*` のサンプルとして `rc_beam_attr_jp_v1` を同梱。
- 配筋（Rebar set）のレイアウト検査/更新コマンドを追加:
  - `rebar_layout_inspect`（read）
  - `rebar_layout_update`（write）
  - `rebar_layout_update_by_host`（write）
- マッピングの動作検証用に `rebar_mapping_resolve`（read）を追加（自動配筋パイプライン無しでもマッピングを検証可能）。

### 注意（現時点）
- レイアウト更新は **shape-driven** の Rebar set のみ対象です（free-form は `NOT_SHAPE_DRIVEN` としてスキップ）。
- `minimum_clear_spacing` は Revit API バージョン差があり得るため、リフレクションで **ベストエフォート**実装（未対応環境ではエラーコードを返します）。
- `RebarMapping.json` の探索順（概要）:
  - `REVITMCP_REBAR_MAPPING_PATH`
  - アドインフォルダ（推奨: `Resources\\RebarMapping.json` または DLL と同じフォルダ）
  - `%LOCALAPPDATA%\\RevitMCP\\RebarMapping.json`（上書き/キャッシュ）
  - `%USERPROFILE%\\Documents\\Codex\\Design\\RebarMapping.json`（開発用）

### 実装変更（主なファイル）
- `RevitMCPAddin/Core/RebarMappingService.cs`
- `RevitMCPAddin/Core/RebarArrangementService.cs`
- `RevitMCPAddin/Commands/Rebar/RebarLayoutInspectCommand.cs`
- `RevitMCPAddin/Commands/Rebar/RebarLayoutUpdateCommand.cs`
- `RevitMCPAddin/Commands/Rebar/RebarLayoutUpdateByHostCommand.cs`
- `RevitMCPAddin/Commands/Rebar/RebarMappingResolveCommand.cs`
- `RevitMCPAddin/RebarMapping.json`
- `RevitMCPAddin/RevitMcpWorker.cs`
- `RevitMCPAddin/RevitMCPAddin.csproj`
- `Manuals/FullManual/rebar_layout_inspect.md`
- `Manuals/FullManual/rebar_layout_update.md`
- `Manuals/FullManual/rebar_layout_update_by_host.md`
- `Manuals/FullManual/rebar_mapping_resolve.md`
- `Manuals/FullManual_ja/rebar_layout_inspect.md`
- `Manuals/FullManual_ja/rebar_layout_update.md`
- `Manuals/FullManual_ja/rebar_layout_update_by_host.md`
- `Manuals/FullManual_ja/rebar_mapping_resolve.md`
- `Manuals/Commands/Commands_Index.all.en.md`
- `Manuals/Commands/commands_index.json`
- `Manuals/Commands/revitmcp_commands_extended.jsonl`
- `Manuals/Commands/revitmcp_commands_full.jsonl`

## 2025-12-25 — AutoRebar: Recipe Ledger / Sync / Delete&Recreate（梁の主筋延長・折り曲げ等のオプション追加）

### 目的
- ホスト（柱/梁）のパラメータ変更は Rebar 要素に自動反映されないため、**Delete & Recreate** を安全に実行できるようにする。
- 直近の生成状態と一致しているかを **署名（SHA-256）** で判定できるようにする（監査/差分検出用）。

### 追加コマンド
- `rebar_sync_status`（read）: 現在のレシピ署名と、Revit内 ledger に保存された署名を比較して `isInSync` を返す。
- `rebar_regenerate_delete_recreate`（write）: 「タグ付きのツール生成鉄筋」を削除し、再作成し、ledger を更新する。
  - 削除は安全のため `deleteMode=tagged_only` のみ（ホスト内＋Commentsタグ一致のみ削除）。

### 実装の要点
- Ledger は **DataStorage + ExtensibleStorage** に、1フィールド（JSON文字列）として保存。
  - `rebar_sync_status`（read）は DataStorage を新規作成しない（read-only 安全性）。
  - `rebar_regenerate_delete_recreate`（write）は ledger が無い場合のみ作成（Transaction内）。
- Schema 作成時に `SchemaName` の競合等で失敗した場合は、**GUID は維持したまま**ユニークな SchemaName での作成を再試行（`LEDGER_ENSURE_FAILED` 回避）。
- Recipe Signature は **canonical JSON**（JObjectキーを再帰的にソート）を SHA-256 で署名。
  - profile/options/mapping値/plan actions（曲線＋layout）が変わると署名が変化します。
- トランザクションはホスト単位に分離し、失敗時はそのホストのみロールバック。

### 梁のオプション強化（計画/作成の足場機能）
- 主筋の軸方向を bbox から延長/短縮: `beamMainBarStartExtensionMm` / `beamMainBarEndExtensionMm`
- 端部90度折り曲げ（線分追加で表現）: `beamMainBarStartBendLengthMm` / `beamMainBarEndBendLengthMm` + `beamMainBarStartBendDir` / `beamMainBarEndBendDir`
- スターラップの始点コーナー選択: `beamStirrupStartCorner`

### 実装変更（主なファイル）
- `RevitMCPAddin/Core/Rebar/RebarRecipeSignature.cs`
- `RevitMCPAddin/Core/Rebar/RebarRecipeLedgerStorage.cs`
- `RevitMCPAddin/Core/Rebar/RebarDeleteService.cs`
- `RevitMCPAddin/Core/Rebar/RebarRecipeService.cs`
- `RevitMCPAddin/Core/RebarAutoModelService.cs`
- `RevitMCPAddin/Commands/Rebar/RebarSyncStatusCommand.cs`
- `RevitMCPAddin/Commands/Rebar/RebarRegenerateDeleteRecreateCommand.cs`
- `RevitMCPAddin/RevitMcpWorker.cs`
- `Manuals/FullManual/rebar_sync_status.md`
- `Manuals/FullManual/rebar_regenerate_delete_recreate.md`
- `Manuals/FullManual_ja/rebar_sync_status.md`
- `Manuals/FullManual_ja/rebar_regenerate_delete_recreate.md`
- `Manuals/Commands/Commands_Index.all.en.md`
- `Manuals/Commands/commands_index.json`
- `Manuals/Commands/revitmcp_commands_extended.jsonl`
- `Manuals/Commands/revitmcp_commands_full.jsonl`

## 2025-12-25 — Rebar: 任意IDの削除/移動コマンドを追加（Bモード対応）

### 目的
- `rebar_regenerate_delete_recreate` は「タグ付き・ホスト内限定」の安全削除でしたが、
  既存モデルに対して **任意の Rebar 要素ID** を直接操作（削除/移動）したいケースがあるため。

### 追加コマンド
- `delete_rebars`（alias: `delete_rebar`）
  - Rebar系（`Rebar`/`RebarInSystem`/`AreaReinforcement`/`PathReinforcement`）のみ削除（それ以外は `skipped`）。
  - `dryRun` と `batchSize`（分割トランザクション）対応。
- `move_rebars`（alias: `move_rebar`）
  - Rebar系のみ移動（それ以外は `skipped`）。
  - `offsetMm` または `dx/dy/dz`（mm）、さらに `items[]` で要素ごとのオフセット指定に対応。

### 互換（小変更）
- `rebar_layout_inspect` / `rebar_layout_update` が `elementIds`（エイリアス入力）も受け付けるように改善。

### 実装変更（主なファイル）
- `RevitMCPAddin/Commands/Rebar/DeleteRebarsCommand.cs`
- `RevitMCPAddin/Commands/Rebar/MoveRebarsCommand.cs`
- `RevitMCPAddin/RevitMcpWorker.cs`
- `RevitMCPAddin/RevitMCPAddin.csproj`
- `Manuals/FullManual/delete_rebars.md`
- `Manuals/FullManual/move_rebars.md`
- `Manuals/FullManual_ja/delete_rebars.md`
- `Manuals/FullManual_ja/move_rebars.md`

---

## 2026-01-20 (later)

### ツールスクリプト改善
- `Scripts/Reference/send_revit_command_durable.py`  
  - ポーリングのリトライ上限を時間帯で自動切替（07:00〜23:00: 3回 / 23:00〜07:00: 10回）。  
  - 上書き方法: `--max-attempts` または環境変数 `MCP_MAX_ATTEMPTS`（全時間帯共通）、`MCP_MAX_ATTEMPTS_DAY` / `MCP_MAX_ATTEMPTS_NIGHT`（時間帯別）。

### インストール手順強化
- `Scripts/Reference/install_revitmcp_safe.ps1`
  - Revit / RevitMCPServer を停止してから /MIR コピー（addin本体→%APPDATA%\\...\\RevitMCPAddin、server→server）。
  - サーバーexeのSHA256ハッシュを検証。ロックや部分コピーによる破損を防止。

### 追加コマンド（Transform系）
- `element.copy_elements`（平行移動コピー。units=mm/m/ft、failIfPinned対応）
- `element.mirror_elements`（平面ミラー、コピー有無切替、precheck/pinnedチェック）
- `element.array_linear`（直線配列、関連あり/なし、anchor Second/Last、view指定、units対応）
- `element.array_radial`（放射配列、関連あり/なし、angleUnits=rad/deg、anchor Second/Last、view指定、units対応）
- `element.pin_element` / `element.pin_elements`（ピン/解除、continueOnErrorオプション）

---

## 2026-01-23

### Detail Line 削除の改善
- `view.delete_detail_line`（alias: `delete_detail_line`）が `elementIds[]` を受け付けるようにし、**一括削除**に対応しました。
  - 単体は従来通り `elementId`。
  - 安全のため、対象は `OST_Lines` の view-specific な詳細線のみ（それ以外は `skipped`）。

### 実装変更（主なファイル）
- `RevitMCPAddin/Commands/AnnotationOps/DetailLineCommands.cs`
- `Manuals/FullManual/delete_detail_line.md`
- `Manuals/FullManual_ja/delete_detail_line.md`
- `Manuals/FullManual/commands.manifest.json`
- `Manuals/Commands/revitmcp_commands_full.jsonl`
- `Manuals/Commands/revitmcp_commands_extended.jsonl`





## 2026-02-06 Brace UI: brace type dropdown filter + selection-level fixes

### 目的
- ブレース配置UIのタイプドロップダウンを、ユーザー指定条件で絞り込み可能にする。

### 変更概要
- place_roof_brace_from_prompt に brace type フィルタ条件を追加（未指定なら全件自動検出）。
- 条件: contains/exclude/family/typeName を AND で評価。

### 詳細

