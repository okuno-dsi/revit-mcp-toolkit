# 参考にすべき失敗事例（Diffビュー作成・着色・雲マーク）

本書は、RSL1 Diff ビューの作成や差分の可視化（着色・雲マーク）を行う際に発生した典型的な失敗と、その原因・対策を整理したものです。今後の運用で同様の事象を未然に防ぐための参照用ドキュメントです。

## 失敗パターンと原因

- ポート交錯と厳格ガードの不一致
  - 症状: `open_views`/`select_elements` が `EXPECT_VIEW_NOT_FOUND` / `EXPECT_VIEW_NOT_ACTIVE` を返す。
  - 原因: `ensure_compare_view` が返した `viewId` を、別ポートに対して使用してしまうケースや、`__expectViewId` ガードにより「該当ドキュメントに存在しない/アクティブでない」判定となる。
  - 対策: ポート専用スクリプトに分離。生成直後は「名前→ID」で再解決してから後続操作。生成系の呼び出しでは `__expect*` ガードは付与しない。

- ビューIDの再解決不足（IDの使い回し）
  - 症状: 生成直後に取得した `viewId` を後続で参照した際に存在しない扱いとなる。
  - 原因: 名前重複・テンプレートの影響・増分複製等でIDが都度異なる場合がある。
  - 対策: ビュー操作ごとに `get_views(detail=true)` で「名前→ID」を再解決。`onNameConflict='returnExisting'` を既定にしてID変動を抑制。

- 選択依存の雲作図における選択の揮発
  - 症状: `create_obb_cloud_for_selection` が `NO_SELECTION` を返す。
  - 原因: `select_elements` の後に別コマンドを挟むと選択がクリアされやすい環境がある。
  - 対策: 可能なら選択に依存せず、`create_revision_cloud_for_element_projection` の `elementIds[]` で直接作図。やむを得ず選択経由なら直後に `stash_selection`→雲作図を行う。

- レスポンス形の取り違え
  - 症状: RSL1 が存在するのに「見つからない」と判定。
  - 原因: `get_views` の結果階層を `result.views` と誤参照（正: `result.result.views`）。
  - 対策: 各RPCの戻り形を固定化・ユーティリティで抽出。テストで検証。

- ビューアクティブ化の取り扱い
  - 症状: `select_elements` 実行時に `EXPECT_VIEW_NOT_ACTIVE`。
  - 原因: 期待するビューがアクティブ化されていない。
  - 対策: `activate_view` もしくは `open_views(viewIds=[...])` を直前に実施。必要であれば一時的にガードを外し、直後に `list_open_views` で確認。

## 安定動作のための原則（運用）

- ポート専用スクリプト
  - 例: `snapshot_5210.ps1` / `snapshot_5211.ps1`、`ensure_diff_view_5211.ps1` のように完全分離。
  - ベースURL/ログ出力/保存先ディレクトリをポートごとに固定。

- ビュー生成の方針
  - `ensure_compare_view` は `onNameConflict='returnExisting'` を標準。増殖が必要な場合のみ `increment` を使う。
  - 生成直後のIDは無条件に正とせず、「名前→ID」再解決のステップを挟む。

- 選択依存の除外
  - 雲マークは `create_revision_cloud_for_element_projection` に `elementIds[]`, `viewId`, `mode='obb'` を渡す方式へ。選択は可視化確認など限定用途に。

- ガードの適用箇所
  - 生成・作図などIDが変化しうる箇所ではガードを付けない/最小限。
  - 安定したビュー・要素操作では `__expectProjectGuid` / `__expectViewId` を適宜付与して混線を検知。

- 逐次検証
  - 各ステップ後に軽い検証を挟む（`list_open_views`、`list_revision_clouds_in_view`、`get_element_info(includeVisual=true)` など）。

## 最小手順（例: 5211で RSL1 Diff の作成のみ）

1) RSL1 を特定
```
python send_revit_command_durable.py --port 5211 --command get_views --params '{"includeTemplates":false, "detail":true}' --output-file get_views_5211.json
# JSONの result.result.views から name='RSL1' の viewId を取得
```

2) Diffビュー作成（重複回避）
```
python send_revit_command_durable.py --port 5211 --command ensure_compare_view \
  --params '{"baseViewId":5133620, "desiredName":"RSL1 Diff", "withDetailing":true, "detachTemplate":true, "onNameConflict":"returnExisting"}' \
  --output-file ensure_compare_view_5211.json
```

3) 必要ならアクティブ化
```
python send_revit_command_durable.py --port 5211 --command open_views --params '{"viewIds":[<diffViewId>]}'
```

## トラブルシューティング・チェックリスト

- 返却コードに `EXPECT_*` が含まれるか
  - `EXPECT_VIEW_NOT_FOUND` / `EXPECT_VIEW_NOT_ACTIVE` / `EXPECT_PROJECT_GUID_MISMATCH` など。
  - 解決: ガードを外して再試行、もしくは `get_views` / `get_project_info` で前提を再確認。

- 雲マークが出ない
  - `NO_SELECTION` の場合は選択依存が疑い。`elementIds[]` 直指定の作図コマンドへ切替。
  - リビジョンクラウドカテゴリの可視化設定を確認（コマンドでは自動で可視化解除を試行）。

- ビューが増殖する
  - `onNameConflict='increment'` を常用していないか確認。標準は `returnExisting`。

## 今後のコード/スクリプト方針（要点）

- RPC抽出ユーティリティで `result.result` の正規化を一元化。
- 各コマンドの直後に要約ログ（counts/ids）を保存する運用を標準化。
- 生成→参照の流れでは常に「名前→ID」で再解決するヘルパを用意。
- 選択不要の作図・可視化APIを優先採用。

---
最終更新: 2025-11-09
