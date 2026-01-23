# Python Runtime (Local-only)

このリポジトリには **Python 実行ランタイム（バイナリ含む）** を含めません。  
ローカル環境で使用する場合は、下記の構成を `RevitMCPAddin/python/` に配置してください。

## 必須フォルダ/ファイル（例）
- `RevitMCPAddin/python/Lib/`（Python 標準ライブラリ一式）
- `RevitMCPAddin/python/Lib/site-packages/requests/`
- `RevitMCPAddin/python/Lib/site-packages/urllib3/`
- `RevitMCPAddin/python/Lib/site-packages/certifi/`
- `RevitMCPAddin/python/Lib/site-packages/charset_normalizer/`（`.pyd` を含む）
- `RevitMCPAddin/python/Lib/site-packages/idna/`
- 上記に対応する `*.dist-info/` フォルダ群

## 注意
- `*.pyd` などの **バイナリファイルは GitHub にアップしません**。
- このフォルダは `.gitignore` で除外済みです。
- 実際の配置物はローカル環境の Python ランタイムからコピーしてください。
