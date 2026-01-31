# Python Runner Scripts

このフォルダは **ユーザー向けの汎用スクリプト置き場** です。

- **ここに置いたスクリプトが Python Runner のライブラリ対象** になります。
- **Work/<ProjectName>_<docKey>/python_script** は「プロジェクト固有のスクリプト」用です。
- **Manuals/Scripts** は AI 参照用（テンプレ・検証用）であり、Python Runner 用の保管場所ではありません。
- **PowerShell スクリプト（.ps1）は Python Runner では実行できないため、`Tools/PowerShellScripts/` に集約**します。

運用の目安:
- 汎用・再利用したいスクリプト → ここ
- プロジェクト専用・一時的 → Work/<ProjectName>_<docKey>/python_script
- AI が参照するテンプレ/検証用 → Manuals/Scripts
