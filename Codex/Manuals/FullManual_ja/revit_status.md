# revit.status

サーバー側のみで完結するステータス／テレメトリ取得コマンドです。Revit がビジーでも **キューに積まずに即時応答** します。

## 返す内容

- サーバー情報: `serverPid`, `serverPort`, `startedAtUtc`, `uptimeSec`
- キュー件数: `queue.countsByState` と `queuedCount/runningCount/dispatchingCount`
- 実行中ジョブ（推定）: `activeJob`
- 直近エラー（推定）: `lastError`
- stale cleanup（best-effort）: `staleCleanup`
  - サーバーがクラッシュ等したあとに「RUNNING が残留」して誤認しないよう、極端に古い `RUNNING/DISPATCHING` を `DEAD`（`error_code: "STALE"`）として回収することがあります。
  - しきい値は `REVIT_MCP_STALE_INPROGRESS_SEC` で変更できます（既定: 21600 秒）。

## 呼び出し方法

durable helper:
```powershell
python .\\Manuals\\Scripts\\send_revit_command_durable.py --port 5210 --command revit.status
```

JSON-RPC:
```json
{ "jsonrpc":"2.0", "id":1, "method":"revit.status", "params":{} }
```


