# 建築基準法 SQLite バンドル

このパッケージには、Revit MCP / CodexUI から read-only で参照するための建築基準法 DB バンドルを同梱できます。

## 配置先

- installer 配置後の既定:
  - `Documents/Revit_MCP/SQLite/Legal/BuildingCode`

## 内容

- `xml_law_index.sqlite`
  - 建築基準法および関連法規 XML を索引化した DB
- `gov_document_text_index.sqlite`
  - 政府 HTML/TXT 資料の索引 DB
- `gov_document_pdf_index.sqlite`
  - 政府 PDF 資料の索引 DB
- `bundle_manifest.json`
  - 収録内容と作成日時のメタデータ

必要に応じて、元の法令 XML も同じフォルダ直下に配置します。

## 使い方

- 法令一覧:
  - `python Codex/Tools/query_building_law_sqlite.py --list-laws --limit 20`
- 本文検索:
  - `python Codex/Tools/query_building_law_sqlite.py --query "建築基準法 防火区画" --limit 10`
- 条文参照付き:
  - `python Codex/Tools/query_building_law_sqlite.py --query "避難階段" --include-references --references-limit 5`

## 既定パス解決

検索スクリプトは次を順に探します。

1. 環境変数 `REVIT_MCP_BUILDING_LAW_SQLITE_ROOT`
2. `Documents/Revit_MCP/SQLite/Legal/BuildingCode`
3. `Codex/../SQLite/Legal/BuildingCode`

## 注意

- 本バンドルは read-only の静的資料です。最新法令を自動追従しません。
- 回答時には、同梱時点の情報であることを明示してください。
- DB 自体は検索補助であり、最終判断は原典確認を前提にしてください。
