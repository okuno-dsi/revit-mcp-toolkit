# ChangeLog 2026-01-08 (AutoRebar: cover from host instance)

## 変更点（要約）
- `rebar_plan_auto` の被り厚さ（cover）を **ホスト要素インスタンスの鉄筋被り設定**から直接取得して反映するように改善。
  - 例: `CLEAR_COVER_TOP/BOTTOM/OTHER` → `RebarCoverType.CoverDistance`
  - `RebarMapping.json` のプロファイルに cover のキーが無い場合でも、インスタンス設定があれば反映されます。
- 応答の `hosts[].coversMm` に `sources` を追加し、被り厚さの由来（`hostInstance` / `mapping` / `default`）を返すようにしました。
- さらに、ホストが `かぶり厚-上/下/左/右` のような **面ごとの被り（数値）** を持つ場合、断面方向の配置にはそれを優先して利用し、`hosts[].coverFacesMm` として返すようにしました（best-effort）。
- 被り厚さのパラメータ名がモデル依存で曖昧なケースに備え、被りの読み取りが確定できない（または最小値未満）場合は `ok:false` / `code:"COVER_CONFIRMATION_REQUIRED"` を返して **作図/適用を止める**ようにしました。
  - `options.coverConfirmProceed:true` で明示同意して続行、または `options.coverParamNames`（up/down/left/right）で読むパラメータ名を指定できます。
  - 値が `options.coverMinMm` 未満の場合は、`options.coverClampToMin:true` で最小値へ丸めます（best-effort）。

## 影響するコマンド
- `rebar_plan_auto`
- `rebar_apply_plan`（`rebar_plan_auto` が返す plan を使うため）

## 変更ファイル
- `RevitMCPAddin/Core/RebarAutoModelService.cs`
- `Manuals/FullManual/rebar_plan_auto.md`
- `Manuals/FullManual_ja/rebar_plan_auto.md`
