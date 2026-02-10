# @feature: 最短経路の解析 | keywords: 経路, 解析, PathOfTravel, route
# -*- coding: utf-8 -*-
"""
選択した 2 要素の座標から、route.find_shortest_paths で最短経路を解析します。
※解析のみ。PathOfTravel 要素は作成しません。

使い方:
  python path_find_shortest_between_two_selected.py --port 5210
"""

import argparse
import json
import time
from typing import Any, Dict, Optional

import requests

DEFAULT_TIMEOUT = 30
POLL_INTERVAL = 0.5
POLL_TIMEOUT = 60


def detect_rpc_endpoint(base_url: str) -> str:
    for ep in ("/rpc", "/jsonrpc"):
        url = f"{base_url}{ep}"
        try:
            r = requests.post(
                url,
                json={"jsonrpc": "2.0", "id": "ping", "method": "help.ping_server", "params": {}},
                timeout=3,
            )
            if r.status_code == 200:
                return url
        except Exception:
            continue
    return f"{base_url}/rpc"


def unwrap_result(obj: Any) -> Any:
    if not isinstance(obj, dict):
        return obj
    if "result" in obj and isinstance(obj["result"], dict):
        inner = obj["result"]
        if "result" in inner and isinstance(inner["result"], dict):
            return inner["result"]
        return inner
    return obj


def poll_job(base_url: str, job_id: str) -> Dict[str, Any]:
    job_url = f"{base_url}/job/{job_id}"
    deadline = time.time() + POLL_TIMEOUT
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
                try:
                    return unwrap_result(json.loads(result_json))
                except Exception:
                    return {"ok": True, "result_json": result_json}
            return {"ok": True}
        if state in ("FAILED", "TIMEOUT", "DEAD"):
            raise RuntimeError(job.get("error_msg") or state)
        time.sleep(POLL_INTERVAL)
    raise TimeoutError(f"job polling timed out (jobId={job_id})")


class RpcClient:
    def __init__(self, base_url: str):
        self.base_url = base_url
        self.endpoint = detect_rpc_endpoint(base_url)

    def call(self, method: str, params: Optional[dict] = None) -> Any:
        payload = {
            "jsonrpc": "2.0",
            "id": f"req-{int(time.time()*1000)}",
            "method": method,
            "params": params or {},
        }
        r = requests.post(self.endpoint, json=payload, timeout=DEFAULT_TIMEOUT)
        r.raise_for_status()
        data = r.json()
        if "error" in data:
            raise RuntimeError(data["error"])
        result = data.get("result", {})
        if isinstance(result, dict) and result.get("queued"):
            job_id = result.get("jobId") or result.get("job_id")
            if not job_id:
                raise RuntimeError("queued but jobId missing")
            return poll_job(self.base_url, job_id)
        return unwrap_result(result)


def get_selected_ids(client: RpcClient) -> list:
    ctx = client.call("help.get_context", {"includeSelectionIds": True, "maxSelectionIds": 10})
    data = ctx.get("data") if isinstance(ctx, dict) else None
    if not isinstance(data, dict):
        return []
    return data.get("selectionIds") or []


def get_element_info(client: RpcClient, element_id: int) -> dict:
    info = client.call("element.get_element_info", {"elementIds": [element_id]})
    if not isinstance(info, dict):
        return {}
    elems = info.get("elements") or []
    return elems[0] if elems else {}


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=5210)
    ap.add_argument("--view-id", type=int, default=0, help="対象ビューID（省略時はActiveView）")
    args = ap.parse_args()

    base_url = f"http://127.0.0.1:{args.port}"
    client = RpcClient(base_url)

    ids = get_selected_ids(client)
    if len(ids) < 2:
        print("選択要素が2つ必要です。")
        return 1

    a_id, b_id = ids[0], ids[1]
    a = get_element_info(client, a_id)
    b = get_element_info(client, b_id)
    start = a.get("coordinatesMm") or {}
    end = b.get("coordinatesMm") or {}
    if not start or not end:
        print("開始/終了点の座標が取得できません。")
        return 1

    params = {
        "start": {"x": start.get("x", 0), "y": start.get("y", 0), "z": start.get("z", 0)},
        "end": {"x": end.get("x", 0), "y": end.get("y", 0), "z": end.get("z", 0)},
    }
    if args.view_id:
        params["viewId"] = args.view_id

    result = client.call("route.find_shortest_paths", params)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
