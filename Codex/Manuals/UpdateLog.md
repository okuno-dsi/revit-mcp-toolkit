# Update Log (Manual + Add-in)

## 2026-01-09 Server: revit.status の “ghost RUNNING” 回収 + docs系エンドポイントの canonical-only 既定化
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

## 2026-01-08 Docs: capabilities（機械可読なコマンド一覧）を追加
- サーバーに `GET /debug/capabilities` を追加し、現在把握しているコマンド実装状況（最後に受信したマニフェスト由来）を JSON で返すようにしました。
- サーバー側で `docs/capabilities.jsonl`（1行=1コマンド）を自動生成するようにしました（マニフェスト読み込み/登録時、best-effort）。
- 修正: `since` が混在しないよう、サーバー注入の `revit.status` / `status` / `revit_status` は「最後に受信したマニフェスト内でもっとも多い `since`」に合わせます（マニフェストが空の場合のみサーバー側 `since` にフォールバック）。
- 注意:
  - これらは「最後に受信したマニフェスト」を反映します。Revit 起動後にアドインが同ポートのサーバーへ接続してマニフェスト送信していることが前提です。
  - capabilities の各フィールドはスキーマ安定です（値が取得できない場合は安全な既定値で補完されます）。
  - 追加: alias 解決を確実にするため `canonical` フィールドを追加しました（deprecated alias の正規名）。

## 2026-01-08 Step 4: canonical/alias（正規名/従来名）ポリシーを強化
- canonical（正規名）は **namespaced**（`*.*`）を基本とし、従来名は alias として残しつつ `deprecated=true` 扱いに統一。
- `list_commands` / `search_commands` は **canonicalのみを返す**のがデフォルトになりました（`includeDeprecated=true` で deprecated を含める）。
- capabilities では `summary`/`resultExample`/`supportsFamilyKinds`/`since`/`revitHandler` などを欠損させず、機械可読性を優先して自動補完します（best-effort）。
- capabilities の legacy 名整理:
  - `revit.status` を正規名に統一し、`status` / `revit_status` は deprecated alias 扱いに変更。
  - `test_cap` はテスト混入のため除外（出力されません）。
  - `/manifest/register` は同一 `source` の登録を上書き扱いに変更し、古いコマンドが残り続ける問題（stale）を回避。

## 2026-01-08 ViewOps: 3Dビュー作成コマンドの重複整理 + sheet.list を Read 扱いに修正
- `view.create_focus_3d_view_from_selection` を正として統一し、重複して見えていた `view.create_clipping_3d_view_from_selection` は deprecated alias 扱いに整理（エージェントが迷わないように）。
- deprecated alias の `summary` を `deprecated alias of <canonical>` 形式に変更（capabilities上で混乱しないように）。
- `sheet.list`（および alias の `get_sheets`）が kind/transaction を誤って `Write` と推定していたため、推定ロジックを修正して `Read` に統一。
- “末尾一致で辿れない legacy→canonical 変換” は `LegacyToCanonicalOverrides`（明示aliasMap）で解決するように追加（例: `place_view_on_sheet_auto` / `sheet_inspect` / `revit_batch` / `revit_status`）。

## 2026-01-07 AutoRebar: RUG柱（RC_C_B:1C1）向けプロファイル追加 + RebarBarTypeの「径フォールバック」解決

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

## 2026-01-07 UI: Undo履歴の表示名をコマンド別に改善
- Undoスタックに表示される `MCP Ledger Command` を、`MCP <短いラベル>`（コマンド名から自動生成）に変更。

## 2026-01-07 Maintenance: ローカルキャッシュ自動クリーンアップ + 手動コマンド追加
- Revit 起動時に `%LOCALAPPDATA%\\RevitMCP` 配下の「7日より古いキャッシュ」を best-effort で自動削除するようにしました（現行ポート/稼働中ポート推定は除外）。
- 手動実行用に `cleanup_revitmcp_cache`（dryRun 既定）を追加しました。
- `RebarMapping.json` / `RebarBarClearanceTable.json` の探索順を「アドインフォルダ優先」に変更しました（既定は同梱ファイルを使用、`%LOCALAPPDATA%\\RevitMCP` は上書き/キャッシュ扱い）。

### 実装変更（主なファイル）
- `RevitMCPAddin/Core/RebarAutoModelService.cs`
- `RevitMCPAddin/RebarMapping.json`
- `Manuals/FullManual/rebar_plan_auto.md`
- `Manuals/FullManual_ja/rebar_plan_auto.md`
- `Manuals/Runbooks/RUG/Rebar_Params_RUG_SelectedTypes_JA.md`

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
- `Manuals/Scripts/test_failure_handling_overlapping_wall.ps1`（壁重なり警告の再現）
- 既存部屋に重ねて `create_room` 実行（Roomの重複警告→rollbackの再現）

### 互換性・注意
- 既存の応答形式に対して **additive（追記）** のみで、既存クライアントを壊さない方針。
- ダイアログDismissは best effort です（Revit側ダイアログ仕様に依存）。

## 2025-12-24 — ViewWorkspace: 初回スナップショット未存在時のスパム/早期断念を改善

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
  - `Manuals/Scripts/room_finish_compare_to_excel.py`

### 実装変更（主なファイル）
- `RevitMCPAddin/Commands/Room/GetRoomFinishTakeoffContextCommand.cs`
- `RevitMCPAddin/RevitMcpWorker.cs`
- `Manuals/FullManual/get_room_finish_takeoff_context.md`
- `Manuals/FullManual_ja/get_room_finish_takeoff_context.md`

## 2025-12-25 ? get_room_finish_takeoff_context: 床/天井のレベル混入を抑止（既定で同一レベルのみ）

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
