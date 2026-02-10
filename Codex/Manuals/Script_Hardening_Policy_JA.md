# スクリプト運用ルールと堅牢化の基本方針

最終更新: 2025-11-08

## 1) 入力の正当性ガード
- プロジェクト一致: `get_project_info` とスナップショット `project.name/number` を必ず照合。不一致なら中止。
- ビュー解決: `get_views {includeTemplates:false, detail:false}` で完全一致の `viewId` を解決。曖昧一致禁止。
- ペア入力: `modifiedPairs` が 0 件なら可視化処理（着色/雲）は実行しない。

## 2) ビュー正規化を必ず行う
- `activate_view` → `set_view_template { clear:true }` → `show_all_in_view { detachViewTemplate:true, includeTempReset:true, unhideElements:true, clearElementOverrides:false }` → `set_category_visibility`（例: 改訂雲 -2006060 を可視化） → `view_fit`。

## 3) パラメータ渡し
- すべての JSON は `--params-file` で渡す（クォート崩れ防止）。
- `ConvertTo-Json -Depth 60` を共通化（Depth>100はエラー）。
- PowerShell の文字列は `-f` で整形。`${var}` を用いてコロン付きの文字列埋め込みを回避。

## 4) タイムアウト/再試行
- 長い処理（雲作成/一括着色）は 1 件ずつまたは小分割。`--timeout-sec` を十分に、指数バックオフを採用。
- `Retry-After` ヘッダを尊重（send_revit_command_durable.py の挙動に準拠）。

## 5) 冪等性/トランザクション
- Diff ビュー作成は `onNameConflict:'returnExisting'` を徹底。失敗時は既存の `RSL1 Diff` を再解決。
- 雲作成は「作成→コメント設定」を 1件ずつ行い、部分失敗でも後続に影響させない。

## 6) 成功判定と事後検証
- 着色: 実行後 `get_visual_overrides_in_view` もしくは対象件数をログ。
- 雲: 実行後 `get_elements_in_view` で `categoryId=-2006060` を数え、件数差があれば再試行対象を抽出。

## 7) ログ/成果物
- すべての MCP 応答はファイルに保存（`Projects/.../Logs/*.json`）。
- スナップショット/差分/可視化の成果物は日付付きで保存し、最新への symlink 的な `*_latest.*` も併置（上書き注意）。

## 8) エラーと復旧
- 例外/HTTPエラー時は「どのコマンドで/何件/どのID」が落ちたかを要約し、失敗IDのみ再試行。
- 409/5xx はバックオフ後に再送。UI モーダルでの停止はユーザーへ明示アラート。

## 9) 並行実行/競合回避
- 同一ビュー/同一要素への並行可視化を禁止（プロセスロック/ファイルロック）。
- 同時に異なるポートへ投げる場合も、ビュー準備→実行→検証を各ポートで直列化。

## 10) 実行前チェックリスト（例）
- Revit 稼働/MCP応答: `ping_server`, `agent_bootstrap` 確認
- ドキュメント一致: 5210/5211 で `projectName/number` 一致
- ビュー存在: `RSL1` が両方に存在
- 入力ペア: `modifiedPairs` > 0 を確認

## 付録: サンプル
- Diff ビュー準備（抜粋）
```
activate_view { viewId }
set_view_template { viewId, clear:true }
show_all_in_view { viewId, detachViewTemplate:true, includeTempReset:true, unhideElements:true }
set_category_visibility { viewId, categoryIds:[-2006060], visible:true }
```
- 雲作成（1件）
```
create_revision_cloud_for_element_projection {
  viewId, elementId, paddingMm:150, preZoom:'element', restoreZoom:false, mode:'aabb'
}
set_revision_cloud_parameter { elementId: <cloudId>, paramName:'Comments', value: 'Diff: ...' }
```


