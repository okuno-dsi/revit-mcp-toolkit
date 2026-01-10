# 移行ガイド（旧Manuals → 現行Manuals）

このガイドは、従来のマニュアル一式（旧版）から、現行の `Manuals` へ移行するときに「影響がある点」だけを抜き出したものです。

前提（旧版の特定）
- 旧版フォルダ `Manuals1202` が作業環境内で見つからなかったため、このガイドでは `Codex/Manuals.zip`（`2025-12-22` 更新）を旧版スナップショットとして差分抽出しています。
- もし別の旧版（例: `Manuals1202`）がある場合、そのパスを教えてください。差分抽出のベースを差し替えて更新します。

## 結論（まずここだけ）
- 既存のスクリプト/運用は **基本そのまま動きます**（legacy名は alias として残しています）。
- ただし、**コマンド探索の“既定出力”が canonical（ドット付き）中心**になったため、一覧や検索結果をコピペして使う運用は、ドット付き名に寄せるのが安全です。
- エージェント実装は、`/debug/capabilities`（または `docs/capabilities.jsonl`）の `canonical` フィールドで、legacy→canonical を確実に解決してください。
  - `GET /debug/capabilities` は既定で canonical-only（deprecated alias を除外）です。alias も必要な場合は `GET /debug/capabilities?includeDeprecated=1`（必要なら `&grouped=1`）を使用してください。

## 影響がある変更点（破壊的になり得るもの）

### 1) canonical/alias 方針の明確化（コマンド名）
- canonical（正規名）: `*.*` の namespaced 名（例: `sheet.list`）
- legacy（従来名）: alias として残すが `deprecated=true`

影響
- `list_commands` / `search_commands` は **既定で canonical のみ返す**（deprecated alias は返さない）。
  - deprecated も一覧に含めたい場合は `includeDeprecated=true` を指定してください。

推奨
- 新規の自動化/エージェントは canonical だけを使う。
- 既存スクリプトは当面そのままでOK（ただし新規追加/修正は canonical に寄せる）。

### 2) status 系コマンドの正規名
- 正: `revit.status`
- alias（deprecated）: `status`, `revit_status`

影響
- 旧スクリプトが `status` を呼んでいても動作はしますが、探索結果は canonical 寄りになるため、今後は `revit.status` を推奨します。

### 3) 旧名→正名が“末尾一致”で辿れない 12 件（リネーム型）
以下は「leaf名が変わっている」ため、`active.EndsWith("." + legacy)` のような suffix 一致だけだと機械的に解決できません。
現行は、capabilities の `canonical`（および `summary: deprecated alias of ...`）で確実に解決できます。

| legacy（旧） | canonical（正） |
|---|---|
| `create_clipping_3d_view_from_selection` | `view.create_focus_3d_view_from_selection` |
| `create_sheet` | `sheet.create` |
| `delete_sheet` | `sheet.delete` |
| `get_sheets` | `sheet.list` |
| `place_view_on_sheet` | `sheet.place_view` |
| `place_view_on_sheet_auto` | `sheet.place_view_auto` |
| `remove_view_from_sheet` | `sheet.remove_view` |
| `replace_view_on_sheet` | `sheet.replace_view` |
| `revit_batch` | `revit.batch` |
| `revit_status` | `revit.status` |
| `sheet_inspect` | `sheet.inspect` |
| `viewport_move_to_sheet_center` | `viewport.move_to_sheet_center` |

エージェント実装の推奨
- サーバーの `GET /debug/capabilities` を取り込み、入力された method が `deprecated=true` の場合は `canonical` に置換して実行する。

### 4) capabilities の `since` 混在の解消（運用上の注意）
- `/debug/capabilities` がサーバー注入する `revit.status/status/revit_status` の `since` を、最後に受信したマニフェスト内の支配的な `since` に揃えるように修正されています。
- これにより、エージェントが `since` をフィルタやキャッシュキーに使う場合でも、混在で誤判定しにくくなっています。

## 差分抽出結果（“影響がある章”の一覧）

旧版（`Manuals.zip`）→ 現行（`Manuals`）の比較結果（抜粋）
- 変更（CHANGED）: 46 files
- 追加（ADDED）: 78 files
- 削除（REMOVED）: 0 files

このうち、移行観点で影響が大きいページ（変更された章）
- `Manuals/FullManual/list_commands.md`
- `Manuals/FullManual/search_commands.md`
- `Manuals/FullManual/describe_command.md`
- `Manuals/FullManual/get_context.md`
- `Manuals/FullManual/agent_bootstrap.md`
- `Manuals/Response_Envelope_EN.md`
- `Manuals/Response_Envelope_JA.md`
- `Manuals/Commands/commands_index.json`（探索/辞書の元データ）
- `Manuals/Commands/Commands_Index.all.en.md`（一覧）

追加されたページ（新機能・参考。既存運用を壊しません）
- `Manuals/FullManual/server_docs_endpoints.md`（`/debug/capabilities` 等）
- `Manuals/FullManual/revit_status.md`（statusの正規名の説明）
- `Manuals/FullManual/create_focus_3d_view_from_selection.md`（3Dビュー作成の整理）

## 既存ユーザー向けの最小対応（おすすめ手順）
1. 既存スクリプトはそのまま使ってOK（急ぎで直す必要はありません）
2. 新規に作る/修正するスクリプトだけ canonical 名に寄せる
3. 「コマンドを探す」用途は、まず `list_commands` / `search_commands` を使い、legacy名が必要なら `includeDeprecated=true`
4. エージェント側は `GET /debug/capabilities` を起動時に一度読み込み、legacy→canonical を自動解決してから実行
