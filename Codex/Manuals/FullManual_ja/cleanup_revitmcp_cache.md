# cleanup_revitmcp_cache

- カテゴリ: System
- 目的: `%LOCALAPPDATA%\\RevitMCP` 配下の古いキャッシュを掃除します（best-effort）。

## 既定で削除対象になるもの
- ルート直下のファイル: `server_state_*.json`, `*.bak_*`, `*.tmp*`（`retentionDays` より古いもの）
- ディレクトリ: `%LOCALAPPDATA%\\RevitMCP\\data\\*`（古いスナップショットフォルダ）
- キュー: `%LOCALAPPDATA%\\RevitMCP\\queue\\p####`（古いポート別キュー）
  - 現行ポートと、ロックファイル（`%LOCALAPPDATA%\\RevitMCP\\locks`）から「稼働中」と推定されるポートは除外します

## 既定で削除しないもの
- `%LOCALAPPDATA%\\RevitMCP\\settings.json`
- `%LOCALAPPDATA%\\RevitMCP\\config.json`
- `%LOCALAPPDATA%\\RevitMCP\\failure_whitelist.json`
- `%LOCALAPPDATA%\\RevitMCP\\RebarMapping.json` / `RebarBarClearanceTable.json`（上書き/キャッシュとして残します）

## 自動クリーンアップ
アドインは Revit 起動時にも同等のクリーンアップを自動実行します（既定 `retentionDays=7`）。

## 使い方
- Method: `cleanup_revitmcp_cache`

### パラメータ
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---:|---:|---|
| dryRun | bool | no | true | true の場合、削除せずに「削除予定」を返します。 |
| retentionDays | int | no | 7 | この日数より古いものが削除対象になります。 |
| maxDeletedPaths | int | no | 200 | `data.deletedPaths` を上限までに切り詰めます（エージェント向け）。 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "cleanup_revitmcp_cache",
  "params": {
    "dryRun": true,
    "retentionDays": 7
  }
}
```

