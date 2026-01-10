# rebar_spacing_check

- カテゴリ: Rebar
- 種別: read
- 目的: 作成済みRebar（モデル上）の中心間距離を実測し、`RebarBarClearanceTable.json` に基づいて離隔チェックします。

## 注意
- 本コマンドは主に **単体鉄筋（配列でないRebar）** のチェックを想定しています。スターラップ/帯筋などの配列（セット）については、まず `rebar_layout_inspect` でレイアウト（spacing等）を確認してください。
- 実測距離は各Rebarの「最長の中心線セグメント」を用いて算出します（best-effort）。
- 比較対象はほぼ平行なもののみ（`parallelDotMin`）です。
- 要求離隔は `RebarBarClearanceTable.json`（径→中心間mm）を使用します。径が異なる2本は `max(reqA, reqB)` を採用します。
 - 応答には `violatingRebarIds`（違反に関与したRebar IDの集合）も含まれるので、`batch_set_visual_override` 等で着色表示に利用できます。

## パラメータ
| 名前 | 型 | 必須 | デフォルト | 説明 |
|---|---|---:|---:|---|
| hostElementIds | int[] | no |  | 指定したホスト要素に含まれるRebarを収集してチェックします。 |
| rebarElementIds | int[] | no |  | 直接Rebarの要素IDを指定してチェックします（host指定が無い場合）。 |
| useSelectionIfEmpty | bool | no | true | IDが空なら選択を使用（Rebarならrebar ids、それ以外はhost ids）。 |
| filter.commentsTagEquals | string | no |  | `Comments` が一致するRebarのみ対象（例: `RevitMcp:AutoRebar`）。 |
| parallelDotMin | number | no | 0.985 | `abs(dot(dirA,dirB)) >= parallelDotMin` のペアのみ比較します。 |
| maxPairs | int | no | 20000 | ホスト毎に解析するペア数上限（超過分はスキップ）。 |
| includePairs | bool | no | false | true の場合、違反ペアの一部を応答に含めます。 |
| pairLimit | int | no | 50 | `includePairs=true` 時に返す違反ペアの最大数。 |

## 例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "rebar_spacing_check",
  "params": {
    "useSelectionIfEmpty": true,
    "filter": { "commentsTagEquals": "RevitMcp:AutoRebar" },
    "includePairs": true,
    "pairLimit": 30
  }
}
```
