# Revit MCP クライアント開発ガイド

Revit MCP（Model Coordination Platform / Automation Server）と通信する Python クライアント実装のための注意点と、共通ユーティリティの標準例をまとめています。これまでの実運用で判明した失敗パターンを踏まえ、再発防止のために必ず参照してください。

## 1) 実装上の重要な注意点（再発防止チェックリスト）

- 通信エンドポイント
  - `/rpc` と `/jsonrpc` は環境により有効・無効が異なります。
  - どちらが有効かを軽いヘルスチェックで自動判別すること（`/rpc` 固定を想定しない）。
- queued 応答の必須対応
  - `result.queued == true` の場合は `jobId` を取り、`/job/{id}` をポーリングする。
  - `202` / `204` は待機を意味するためリトライ。
  - `SUCCEEDED` で `result_json` を JSON 解析し、`FAILED` / `TIMEOUT` / `DEAD` は即例外化。
- 多段 JSON-RPC の `result` 構造に対応
  - 返却が `{ "result": {...} }` / `{ "result": { "result": {...} } }` / `result_json` が JSON-RPC など複数形が存在。
  - 常に `unwrap_result()` で多段ネストを吸収すること。
- サーバ固有メソッドの検出
  - Manuals では `element.query_elements` / `element.search_elements` が中心だが、環境によっては `get_rooms` など専用メソッドが存在。
  - メソッド存在を検出して、存在する場合は優先的に使用する。
- ページングはサーバ仕様に従う
  - `skip` / `count` / `totalCount` がある場合は必ずページング。
  - `maxResults` 依存のみの取得は取りこぼしが起きる。
- タイムアウト / 再試行
  - `/rpc` は `timeout=30s` 程度。
  - `/job` ポーリングは `0.5s` 間隔で `60s` 以上。
  - `502/503/504` は軽くリトライ。
- 出力の安定性
  - 欠落フィールドは `.get("field", "")` を徹底。
  - 数値化は `float()` 例外を捕捉してフォールバック。
- CLI / 環境変数で柔軟切替
  - `--port` / `--endpoint` / `--page-size` を外部から切替可能にする。
- サーバ仕様差異に合わせる
  - Manuals の仕様だけで実装しない。
  - 実サーバを検出し、自動適応する設計を採用する。

## 2) 共通クライアントユーティリティ（Python サンプル）

全スクリプトで再利用する前提の標準ユーティリティ例です。`mcp_client.py` などの名前で保存してください。

```python
# -*- coding: utf-8 -*-
"""
Revit MCP 共通クライアントユーティリティ
- /rpc と /jsonrpc を自動検出
- queued レスポンスに対して job ポーリング
- 多段 result を unwrap
- requests ベース
"""

import time
import json
import requests
from typing import Any, Dict, Optional

DEFAULT_TIMEOUT = 30
POLL_INTERVAL = 0.5
POLL_TIMEOUT = 60

# -------------------------------------------------------
# エンドポイント自動検出
# -------------------------------------------------------
def detect_rpc_endpoint(base_url: str) -> str:
    """/rpc と /jsonrpc のどちらが有効か軽いヘルスチェックで判定"""
    for ep in ("/rpc", "/jsonrpc"):
        url = f"{base_url}{ep}"
        try:
            requests.post(url, json={"jsonrpc": "2.0", "id": "ping", "method": "noop"}, timeout=3)
            return url
        except Exception:
            pass
    return f"{base_url}/rpc"

# -------------------------------------------------------
# JSON-RPC 多段 result を吸収
# -------------------------------------------------------
def unwrap_result(obj: Any) -> Any:
    if not isinstance(obj, dict):
        return obj
    if "result" in obj and isinstance(obj["result"], dict):
        inner = obj["result"]
        if "result" in inner and isinstance(inner["result"], dict):
            return inner["result"]
        return inner
    return obj

# -------------------------------------------------------
# Job polling
# -------------------------------------------------------
def poll_job(base_url: str, job_id: str, timeout_sec: int = POLL_TIMEOUT) -> Dict[str, Any]:
    deadline = time.time() + timeout_sec
    job_url = f"{base_url}/job/{job_id}"

    while time.time() < deadline:
        r = requests.get(job_url, timeout=10)
        if r.status_code in (202, 204):
            time.sleep(POLL_INTERVAL)
            continue

        r.raise_for_status()
        job = r.json()
        state = (job.get("state") or "").upper()

        if state == "SUCCEEDED":
            result_json = job.get("result_json")
            if result_json:
                parsed = json.loads(result_json)
                return unwrap_result(parsed)
            return {"ok": True}

        if state in ("FAILED", "TIMEOUT", "DEAD"):
            raise RuntimeError(job.get("error_msg") or state)

        time.sleep(POLL_INTERVAL)

    raise TimeoutError(f"job polling timed out (jobId={job_id})")

# -------------------------------------------------------
# JSON-RPC 呼び出し（queued 対応）
# -------------------------------------------------------
def rpc(base_url: str, method: str, params: Optional[dict] = None) -> Any:
    endpoint = detect_rpc_endpoint(base_url)

    payload = {
        "jsonrpc": "2.0",
        "id": f"req-{int(time.time()*1000)}",
        "method": method,
        "params": params or {},
    }

    r = requests.post(endpoint, json=payload, timeout=DEFAULT_TIMEOUT)
    r.raise_for_status()
    data = r.json()

    if "error" in data:
        raise RuntimeError(data["error"])

    result = data.get("result", {})

    if isinstance(result, dict) and result.get("queued"):
        job_id = result.get("jobId") or result.get("job_id")
        if not job_id:
            raise RuntimeError("queued but jobId missing")
        return poll_job(base_url, job_id)

    return unwrap_result(result)

# -------------------------------------------------------
# 例：部屋（Rooms）を取得するラッパ
# -------------------------------------------------------
def fetch_rooms_get_rooms(base_url: str, page_size: int = 200) -> list:
    """get_rooms が使える環境向けの Rooms 一括取得"""
    meta = rpc(base_url, "get_rooms", {"skip": 0, "count": 0})
    total = int(meta.get("totalCount", 0) or 0)

    rooms = []
    skip = 0
    while skip < total:
        batch = rpc(base_url, "get_rooms", {"skip": skip, "count": page_size})
        rooms.extend(batch.get("rooms") or [])
        skip += page_size

    return rooms
```

## 3) 使い方例（部屋数を取得）

```python
from mcp_client import rpc, fetch_rooms_get_rooms

base_url = "http://127.0.0.1:5210"

rooms = fetch_rooms_get_rooms(base_url)
print(f"Room count = {len(rooms)}")
```

## 4) まとめ

- 実サーバの実装は Manuals と完全一致しないことがあるため、自動検出・多段 unwrap・queued 対応が必須。
- 本書のユーティリティを標準化すれば、全スクリプトで安定して Revit MCP と通信できる。
- 必要に応じて:
  - `get_rooms` が無い環境向け（`element.query_elements` ベース）
  - `project info` 取得版
  - `create_*` 系の write コマンド版
 などを追加して運用する。
