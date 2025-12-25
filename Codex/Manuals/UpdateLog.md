# Update Log (Manual + Add-in)

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
- 比較JSON（`test_room_finish_takeoff_context_compare_*.json`）から標準化した `SegmentWallTypes` シートを再生成するスクリプトを追加:
  - `Manuals/Scripts/room_finish_compare_to_excel.py`

### 実装変更（主なファイル）
- `RevitMCPAddin/Commands/Room/GetRoomFinishTakeoffContextCommand.cs`
- `RevitMCPAddin/RevitMcpWorker.cs`
- `Manuals/FullManual/get_room_finish_takeoff_context.md`
- `Manuals/FullManual_ja/get_room_finish_takeoff_context.md`
