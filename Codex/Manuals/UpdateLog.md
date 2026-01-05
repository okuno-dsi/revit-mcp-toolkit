# Update Log (Manual + Add-in)

## 2025-12-26 ? ChatRevit 削除と接続手順の簡略化

### 変更概要
- ChatRevit プロキシ（ログ用）をリポジトリから削除。
- 接続ガイド/コマンドの手順を Playbook 直結（5209）に統一。

### 影響範囲（主なファイル）
- `Codex/Manuals/ConnectionGuide/Client_Side_Caching_and_Server_Change_Policy_EN.md`
- `Codex/Manuals/ConnectionGuide/Revit_Connection_OneShot_Quickstart_EN.md`
- `Codex/Manuals/Commands/Revit_Connection_Commands_EN.md`

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
  - `%LOCALAPPDATA%\\RevitMCP\\RebarMapping.json`
  - `%USERPROFILE%\\Documents\\Codex\\Design\\RebarMapping.json`
  - アドインDLLと同じフォルダ

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
