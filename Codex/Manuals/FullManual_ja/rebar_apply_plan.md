# rebar_apply_plan

- カテゴリ: Rebar
- 目的: 自動配筋 plan を適用して鉄筋要素を作成します（または選択から plan を生成してそのまま適用します）。

## 概要
`params.plan` を省略すると、内部で `rebar_plan_auto` 相当の処理を行い、その plan を適用します。

作成した鉄筋の `Comments` に `options.tagComments`（既定 `RevitMcp:AutoRebar`）を設定するため、後から `rebar_layout_update_by_host` の `filter.commentsTagEquals` で一括更新できます。

### 梁（マッピング駆動の配筋属性）について
- `plan` 省略時は `rebar_plan_auto` と同じ計画ロジックを使います。
- `RebarMapping.json` のプロファイルに梁用の論理キーが定義されている場合、`options.beamUseTypeParams=true` で本数/ピッチ等を優先できます（best-effort）。
- `plan` 省略時は `rebar_plan_auto` と同じ梁形状オプション（延長/折り曲げ/スターラップ始点など）も有効です。

## 使い方
- Method: `rebar_apply_plan`

### パラメータ
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---:|---:|---|
| dryRun | bool | no | false | true の場合はモデルを変更せず `{dryRun:true, plan:...}` を返します。 |
| plan | object | no |  | `rebar_plan_auto` が返す plan。 |
| hostElementIds | int[] | no |  | `plan` 省略時のみ使用。 |
| useSelectionIfEmpty | bool | no | true | `plan` 省略時のみ使用。 |
| profile | string | no |  | `plan` 省略時のみ使用。 |
| options | object | no |  | `plan` 省略時のみ使用（`rebar_plan_auto.options` と同様）。 |
| deleteExistingTaggedInHosts | bool | no | false | true の場合、各ホスト内の `Comments == plan.tagComments` の鉄筋系要素を削除してから作成します（既存の自動配筋を更新する用途）。 |

## 注意
- `RebarHostData.IsValidHost=true` のホストのみ対象です。
- ホストごとにトランザクションを分離し、失敗したホストはそのホスト分だけロールバックします。
- 任意形状/任意位置で配筋したい場合は、`plan` を自作し `actions[].curves` を明示的に指定して `rebar_apply_plan` に渡せます（`rebar_plan_auto` は近似の足場機能です）。

## 関連
- `rebar_plan_auto`
- `rebar_layout_update_by_host`
