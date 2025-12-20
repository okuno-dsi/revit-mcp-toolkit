# 変更差分に対するリビジョンクラウド運用（タグ優先・二重防止）

本書は、スナップショットと現在のビューを比較し、変更があった要素に対してリビジョンクラウドを適切に作成・記録するための手順とサンプルをまとめたものです。コード修正以降の運用ポリシーに基づいています。

## 方針（重要）

- 変更のあった要素にリビジョンクラウドを描く。
- 該当要素にタグがある場合は、ユーザーに確認のうえタグにクラウドを付けるか（推奨）、要素本体に付けるかを選択する（オプションで自動化可）。
- 既存のリビジョンクラウド自体が変化していても、それにはクラウドを描かない（ユーザーから明示指示がある場合を除く）。
- リビジョンクラウド作成の根拠となった変更点は CSV に記録する（要素ID・カテゴリ・相違の内容）。
- ユーザー指示がある場合、作成したリビジョンクラウドの「コメント」パラメータに、その変更内容（要約）を追記する。

## 前提

- Revit MCP サーバーが稼働（本書は `Port=5210` を例示）
- PowerShell 7 と Python が利用可能
- 修正済みコマンド（create_revision_cloud 系の堅牢化、タグ投影対応）がビルド済み

## 使用コマンド一覧（代表）

- ビュー・要素取得系
  - `get_current_view`
  - `get_tags_in_view { viewId }`（戻り: tags[] with hostElementId/tagId/location）
  - `get_elements_in_view { viewId, _shape:{ idsOnly:true } }`
  - `get_element_info { elementIds[], rich:true }`
- リビジョン管理・クラウド作成
  - `list_revisions` / `create_default_revision`
- `create_revision_cloud_for_element_projection`（要素/タグ投影。タグは `tagWidthMm/tagHeightMm` 指定に対応）
- `create_revision_cloud`（curveLoops 矩形。投影不可のフォールバック用）
- `get_revision_cloud_parameters` / `set_revision_cloud_parameter`（コメント書込み等）
- `get_types_in_view`（ビュー内で使用されているタイプIDの抽出により、必要なパラメータの一括取得を高速化）
- `get_type_parameters_bulk` / `get_instance_parameters_bulk`（UnitHelper による SI 正規化 + display 併記のバルク取得）

## 手順（標準フロー）

1) ベースラインを決める
   - 例: `Work/Project_5210_B/20251027_123122/view_<viewId>_elements.json`
2) 現在のビューIDを取得 `get_current_view`
3) 現在のビューの要素ID一覧を取得 `get_elements_in_view{ idsOnly:true }`
4) 現在の要素詳細をチャンク取得 `get_element_info{ rich:true }`
5) ベースラインの要素詳細をロード
6) 差分を分類
   - 追加: 今あるがベースにない
   - 削除: ベースにあるが今はない（原則クラウド対象外。CSVには残す）
   - 変更: 共通IDで JSON が異なる（代表的な変更パラメータ名を抽出）
   - 除外: 既存のリビジョンクラウド（クラスやカテゴリでスキップ）
7) リビジョンIDを用意 `list_revisions`→無ければ `create_default_revision`
8) クラウド作成（タグ優先・二重防止）
   - 変更・追加の各要素について：
     - タグ優先（ユーザー指示に従う）
       - タグがある → `get_tag_bounds_in_view` で widthMm/heightMm を取得
       - `create_revision_cloud_for_element_projection { elementId:<tagId>, tagWidthMm, tagHeightMm, paddingMm }`
     - タグが無い/投影不可 → 要素投影クラウド
       - `create_revision_cloud_for_element_projection { elementId:<eid>, paddingMm }`
     - さらに不可 → bbox 矩形クラウド
       - 要素の bbox から mm 長方形の curveLoops を作成し `create_revision_cloud { curveLoops }`
   - 既存のリビジョンクラウドはクラウド対象から除外する（カテゴリ/クラス判定）。
9) CSV 出力（要素ID・カテゴリ・相違の内容）
10) （任意）クラウドコメントに相違内容を追記
    - `set_revision_cloud_parameter { elementId:<cloudId>, name:"Comments"|"コメント", value:"<要約>" }`

## サンプル: PowerShell ワークフロー（抜粋）

以下は概念実行例です。実運用では `--force` を適宜付与してください。

```powershell
# 1) 現在のビュー
python -X utf8 Manuals/Scripts/send_revit_command_durable.py --port 5210 --command get_current_view --output-file Work/tmp/current_view.json

# 2) タグ取得（ビュー内）
python -X utf8 Manuals/Scripts/send_revit_command_durable.py --port 5210 --command get_tags_in_view --params '{"viewId":60521780}' --output-file Work/tmp/tags.json

# 3) 要素IDと詳細
python -X utf8 Manuals/Scripts/send_revit_command_durable.py --port 5210 --command get_elements_in_view --params '{"viewId":60521780, "_shape":{"idsOnly":true}}' --output-file Work/tmp/ids.json
python -X utf8 Manuals/Scripts/send_revit_command_durable.py --port 5210 --command get_element_info --params '{"elementIds":[...],"rich":true}' --output-file Work/tmp/now_part_0.json

# 4) リビジョンID
python -X utf8 Manuals/Scripts/send_revit_command_durable.py --port 5210 --command list_revisions --output-file Work/tmp/revs.json
# 無ければ
python -X utf8 Manuals/Scripts/send_revit_command_durable.py --port 5210 --command create_default_revision --output-file Work/tmp/rev_new.json

# 5) タグ優先クラウド（サイズ付き）
python -X utf8 Manuals/Scripts/send_revit_command_durable.py --port 5210 --command get_tag_bounds_in_view --params '{"viewId":60521780, "tagId":60521957, "inflateMm":100}' --output-file Work/tmp/tag_bounds.json
python -X utf8 Manuals/Scripts/send_revit_command_durable.py --port 5210 --command create_revision_cloud_for_element_projection --params '{"viewId":60521780, "revisionId":60521965, "elementId":60521957, "paddingMm":120, "tagWidthMm":2092.0, "tagHeightMm":1175.7}' --output-file Work/tmp/cloud_for_tag.json

# 6) 要素投影 → 不可なら curveLoops 矩形でフォールバック
python -X utf8 Manuals/Scripts/send_revit_command_durable.py --port 5210 --command create_revision_cloud_for_element_projection --params '{"viewId":60521780, "revisionId":60521965, "elementId":60521941, "paddingMm":150}' --output-file Work/tmp/cloud_for_elem.json
python -X utf8 Manuals/Scripts/send_revit_command_durable.py --port 5210 --command create_revision_cloud --params '{"viewId":60521780, "revisionId":60521965, "curveLoops":[[{"start":{"x":x0,"y":y0,"z":0}, "end":{"x":x1,"y":y0,"z":0}}, ... ]]}' --output-file Work/tmp/cloud_rect.json

# 7) コメント追記（任意）
python -X utf8 Manuals/Scripts/send_revit_command_durable.py --port 5210 --command set_revision_cloud_parameter --params '{"elementId":<cloudId>, "name":"コメント", "value":"壁タイプ変更 + 寸法差分"}' --output-file Work/tmp/set_comment.json
```

## サンプル: Python スクリプト（バッチ自動化）

`Manuals/Scripts/diff_cloud_tagfirst.py` を参照してください。主なオプション:

- `--port 5210`
- `--baseline Work/Project_5210_B/20251027_123122`
- `--tag-mode ask|prefer|never`（既定: ask）
- `--write-comments`（リビジョンクラウドのコメントに要約を書込）
- `--csv out.csv`（CSVの保存先）

実行例:

```bash
  python Manuals/Scripts/diff_cloud_tagfirst.py --port 5210 \
  --baseline Work/Project_5210_B/20251027_123122 \
  --tag-mode prefer --write-comments --csv Work/DiffCloud/out.csv

補足（パフォーマンス向上）
- ビュー内の使用タイプ一覧 `get_types_in_view` → タイプパラメータ `get_type_parameters_bulk` を併用すると、`get_element_info` の詳細展開を省略でき高速化します。
```

## CSV の仕様

- 列: `elementId,category,diff`
  - 追加/削除/変更 を記載
  - 変更の場合、代表的な変更パラメータ名を併記可能（例: `変更: 壁厚/仕上げ/構造`）

## 二重防止・除外ルール

- 既存のリビジョンクラウド要素（クラス/カテゴリが RevisionCloud）は対象外とする。
- 同一ターゲットに対しクラウドを複数回作らないよう、最新実行中に生成した cloudId をメモし、重複判定するか、ユーザー側で重複削除ポリシーを適用する。

## トラブルシューティング

- 投影不可（"No projectable geometry"）
  - タグ優先モードでタグにクラウド、または bbox 矩形クラウドで代替。
- オーバーロード差異による実行失敗
  - 本アドインではバージョン差異に対応済み（反射で解決）。
- Z 入力
  - 入力 Z=0 でも、内部でビューのスケッチ平面に正射影して作図するため問題なし。
