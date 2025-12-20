import argparse
import json
import os
import time
from typing import Any, Dict, List, Optional, Tuple

import requests


HEADERS = {
    "Content-Type": "application/json; charset=utf-8",
    "Accept": "application/json",
}


def ensure_dir(path: str) -> None:
    os.makedirs(path, exist_ok=True)


def save_json(path: str, obj: Any) -> None:
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=2)
    os.replace(tmp, path)


def durable_call_via_proxy(proxy_base: str, revit_port: int, method: str, params: Optional[Dict[str, Any]] = None,
                           timeout: float = 60.0, max_wait_sec: float = 300.0) -> Dict[str, Any]:
    if params is None:
        params = {}
    session = requests.Session()
    session.headers.update(HEADERS)

    enqueue_url = f"{proxy_base}/t/{revit_port}/enqueue"
    payload = {
        "jsonrpc": "2.0",
        "method": method,
        "params": params,
        "id": int(time.time() * 1000),
    }
    r = session.post(enqueue_url, params={"force": 1}, json=payload, timeout=timeout)
    r.raise_for_status()
    data = r.json()
    job_id = data.get("jobId") or data.get("job_id")
    if not job_id:
        return data

    job_url = f"{proxy_base}/t/{revit_port}/job/{job_id}"
    start = time.time()
    etag: Optional[str] = None
    while True:
        headers = dict(HEADERS)
        if etag:
            headers["If-None-Match"] = etag
        jr = session.get(job_url, headers=headers, timeout=timeout)
        if jr.status_code == 304:
            time.sleep(0.25)
            if time.time() - start > max_wait_sec:
                raise TimeoutError(f"Timeout waiting for job {job_id} ({method})")
            continue
        jr.raise_for_status()
        etag = jr.headers.get("ETag") or etag
        o = jr.json()
        st = o.get("state")
        if st == "SUCCEEDED":
            rjson = o.get("result_json")
            if isinstance(rjson, str) and rjson.strip():
                try:
                    return json.loads(rjson)
                except Exception:
                    return {"ok": True, "result": rjson}
            return {"ok": True}
        if st in ("FAILED", "TIMEOUT", "DEAD"):
            raise RuntimeError(f"Job {job_id} failed: {o.get('error_msg')}")
        if time.time() - start > max_wait_sec:
            raise TimeoutError(f"Timeout waiting for job {job_id} ({method})")
        time.sleep(0.35)


def unwrap_result(outer: Dict[str, Any]) -> Dict[str, Any]:
    # Expected shapes:
    # { jsonrpc, id, result: { jsonrpc, id, method, agentId, result: {...} } }
    # or sometimes { jsonrpc, id, result: {...} }
    r = outer.get("result")
    if isinstance(r, dict) and "result" in r:
        return r.get("result") or {}
    if isinstance(r, dict):
        return r
    return outer


def fetch_all_walls(proxy: str, port: int, page_size: int = 500) -> Tuple[int, List[Dict[str, Any]], Dict[str, Any]]:
    skip = 0
    total = None
    walls: List[Dict[str, Any]] = []
    walls_by_id: Dict[str, Any] = {}
    while True:
        outer = durable_call_via_proxy(proxy, port, "get_walls", {"skip": skip, "count": page_size})
        res = unwrap_result(outer)
        page = res.get("walls") or []
        if total is None:
            total = res.get("totalCount")
        by_id = res.get("wallsById") or {}
        if isinstance(by_id, dict):
            walls_by_id.update(by_id)
        if page:
            walls.extend(page)
        if not page or (isinstance(total, int) and len(walls) >= total):
            break
        skip += len(page)
    if total is None:
        total = len(walls)
    return int(total), walls, walls_by_id


def get_current_view_id(proxy: str, port: int) -> Optional[int]:
    outer = durable_call_via_proxy(proxy, port, "get_current_view", {})
    res = unwrap_result(outer)
    vid = res.get("viewId") if isinstance(res, dict) else None
    if isinstance(vid, (int, float)):
        return int(vid)
    return None


def fetch_elements_in_view(proxy: str, port: int, view_id: int, limit: int = 200000) -> Dict[str, Any]:
    # Ask for idsOnly to reduce payload size
    shape = {"idsOnly": True, "page": {"limit": int(limit)}}
    params = {"viewId": int(view_id), "_shape": shape}
    outer = durable_call_via_proxy(proxy, port, "get_elements_in_view", params)
    return unwrap_result(outer)


def main():
    ap = argparse.ArgumentParser(description="Fetch all walls and current-view element IDs via Proxy→Playbook→Revit chain, then save to JSON.")
    ap.add_argument("--proxy", type=str, default="http://127.0.0.1:5221")
    ap.add_argument("--revit-port", type=int, default=5211)
    ap.add_argument("--out-dir", type=str, default=os.path.join("C:", "Users", os.environ.get("USERNAME", "user"),
                    "Documents", "VS2022", "Ver431", "Codex", "Manuals", "Logs"))
    ap.add_argument("--page-size", type=int, default=500)
    ap.add_argument("--ids-limit", type=int, default=200000)
    args = ap.parse_args()

    ensure_dir(args.out_dir)
    base = str(args.revit_port)
    walls_path = os.path.join(args.out_dir, f"walls_all_{base}.json")
    view_ids_path = os.path.join(args.out_dir, f"elements_in_view_active_{base}.json")

    total, walls, walls_by_id = fetch_all_walls(args.proxy, args.revit_port, args.page_size)
    save_json(walls_path, {
        "ok": True,
        "port": args.revit_port,
        "totalCount": total,
        "wallsCount": len(walls),
        "walls": walls,
        "wallsById": walls_by_id,
    })

    view_id = get_current_view_id(args.proxy, args.revit_port)
    if view_id is None:
        raise RuntimeError("Failed to resolve current view id (get_current_view)")
    view_res = fetch_elements_in_view(args.proxy, args.revit_port, view_id, args.ids_limit)
    save_json(view_ids_path, {
        "ok": True,
        "port": args.revit_port,
        "method": "get_elements_in_view",
        "viewId": view_id,
        "result": view_res,
    })

    print(json.dumps({
        "ok": True,
        "saved": {
            "walls": walls_path,
            "elementsInView": view_ids_path,
        },
        "counts": {
            "walls": len(walls),
            "elementsInView": (len(view_res.get("elementIds", [])) if isinstance(view_res, dict) else None)
        },
        "viewId": view_id
    }, ensure_ascii=False))


if __name__ == "__main__":
    main()
