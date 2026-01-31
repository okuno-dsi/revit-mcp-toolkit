# @feature: restore visible set from snapshot | keywords: ビュー, タグ, スナップショット
import argparse
import json
import os
import time
from typing import Any, Dict, List, Optional, Set

import requests


HEADERS = {
    "Content-Type": "application/json; charset=utf-8",
    "Accept": "application/json",
}


def load_json(path: str) -> Any:
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def durable_call(proxy_base: str, revit_port: int, method: str, params: Dict[str, Any], timeout: float = 60.0) -> Dict[str, Any]:
    s = requests.Session(); s.headers.update(HEADERS)
    enq = f"{proxy_base}/t/{revit_port}/enqueue"
    body = {"jsonrpc": "2.0", "id": int(time.time()*1000), "method": method, "params": params}
    r = s.post(enq, params={"force": 1}, json=body, timeout=timeout); r.raise_for_status()
    data = r.json(); job = data.get("jobId") or data.get("job_id")
    if not job:
        return data
    job_url = f"{proxy_base}/t/{revit_port}/job/{job}"
    etag = None; start = time.time()
    while True:
        h = dict(HEADERS); 
        if etag: h["If-None-Match"] = etag
        jr = s.get(job_url, headers=h, timeout=timeout)
        if jr.status_code == 304: 
            time.sleep(0.2); 
            if time.time()-start > 180: raise TimeoutError("Timeout waiting job")
            continue
        jr.raise_for_status(); etag = jr.headers.get("ETag") or etag
        o = jr.json(); st = o.get("state")
        if st == "SUCCEEDED":
            txt = o.get("result_json")
            if isinstance(txt, str) and txt.strip():
                try: return json.loads(txt)
                except Exception: return {"ok": True, "result": txt}
            return {"ok": True}
        if st in ("FAILED","TIMEOUT","DEAD"):
            raise RuntimeError(f"Job failed: {o.get('error_msg')}")
        if time.time()-start > 180: raise TimeoutError("Timeout waiting job")
        time.sleep(0.25)


def unwrap(outer: Dict[str, Any]) -> Dict[str, Any]:
    r = outer.get("result")
    if isinstance(r, dict) and "result" in r: return r.get("result") or {}
    if isinstance(r, dict): return r
    return outer


def main():
    ap = argparse.ArgumentParser(description="Restore visible set in active view from a prior snapshot of elementIds by hiding everything else.")
    ap.add_argument("--proxy", default="http://127.0.0.1:5221")
    ap.add_argument("--revit-port", type=int, default=5211)
    ap.add_argument("--snapshot", required=True, help="Path to elements_in_view_active_<port>.json saved earlier")
    ap.add_argument("--batch", type=int, default=5000)
    args = ap.parse_args()

    snap = load_json(args.snapshot)
    prev_ids = set(snap.get("result", {}).get("elementIds", [])) if isinstance(snap, dict) else set()
    if not prev_ids:
        raise SystemExit("Snapshot missing or empty elementIds")

    # Resolve current view and its visible IDs now
    outer_cv = durable_call(args.proxy, args.revit_port, "get_current_view", {})
    view_id = unwrap(outer_cv).get("viewId")
    if not isinstance(view_id, int):
        raise SystemExit("Failed to get current viewId")

    shape = {"idsOnly": True, "page": {"limit": 500000}}
    outer_now = durable_call(args.proxy, args.revit_port, "get_elements_in_view", {"viewId": view_id, "_shape": shape})
    now_ids = set(unwrap(outer_now).get("elementIds", []))

    to_hide = list(now_ids - prev_ids)
    if not to_hide:
        print(json.dumps({"ok": True, "msg": "No difference; nothing to hide", "viewId": view_id}))
        return

    # Hide in batches
    total = 0; b = max(1, int(args.batch))
    for i in range(0, len(to_hide), b):
        chunk = to_hide[i:i+b]
        _ = durable_call(args.proxy, args.revit_port, "hide_elements_in_view", {"viewId": view_id, "elementIds": chunk})
        total += len(chunk)
    print(json.dumps({"ok": True, "viewId": view_id, "hidden": total}))


if __name__ == "__main__":
    main()

