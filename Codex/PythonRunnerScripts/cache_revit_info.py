# @feature: cache revit info | keywords: タグ
import argparse
import json
import os
import time
from typing import Any, Dict, Optional

import requests


HEADERS = {
    "Content-Type": "application/json; charset=utf-8",
    "Accept": "application/json",
}


def ensure_dir(path: str) -> None:
    os.makedirs(path, exist_ok=True)


def _json(obj: Any) -> str:
    return json.dumps(obj, ensure_ascii=False)


def durable_call_via_proxy(proxy_base: str, revit_port: int, method: str, params: Optional[Dict[str, Any]] = None,
                           timeout: float = 30.0, max_wait_sec: float = 120.0) -> Dict[str, Any]:
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
        # immediate result path
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
            # not modified; brief sleep
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


def save_json(path: str, obj: Any) -> None:
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=2)
    os.replace(tmp, path)


def load_json(path: str) -> Any:
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def main():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    repo_root = os.path.abspath(os.path.join(script_dir, "..", ".."))
    default_out_dir = os.path.join(repo_root, "Work")
    ap = argparse.ArgumentParser(description="Fetch and cache Revit project/document info via Proxy→Playbook→Revit chain.")
    ap.add_argument("--proxy", type=str, default="http://127.0.0.1:5221", help="Proxy base URL")
    ap.add_argument("--revit-port", type=int, default=5211, help="Target Revit MCP port")
    ap.add_argument("--out-dir", type=str, default=default_out_dir, help="Output directory for JSON cache (prefer Work/<Project>_<Port>/Logs)")
    ap.add_argument("--refresh", action="store_true", help="Force refresh even if cache exists")
    ap.add_argument("--ttl-sec", type=int, default=0, help="Cache TTL (seconds). 0 = no TTL (always reuse if present)")
    ap.add_argument("--what", type=str, default="all", choices=["all", "project", "documents"], help="What to fetch")
    args = ap.parse_args()

    ensure_dir(args.out_dir)
    ts = int(time.time())
    base_name = f"{args.revit_port}"
    proj_path = os.path.join(args.out_dir, f"project_info_{base_name}.json")
    docs_path = os.path.join(args.out_dir, f"open_documents_{base_name}.json")

    def cache_valid(path: str) -> bool:
        if not os.path.exists(path):
            return False
        if args.ttl_sec and args.ttl_sec > 0:
            age = time.time() - os.path.getmtime(path)
            return age <= args.ttl_sec
        return True

    results = {}

    if args.what in ("all", "project"):
        if not args.refresh and cache_valid(proj_path):
            proj = load_json(proj_path)
        else:
            outer = durable_call_via_proxy(args.proxy, args.revit_port, "get_project_info")
            # expected outer: { jsonrpc, id, result: { jsonrpc, id, method, agentId, result: {...} } }
            inner = outer.get("result", {}).get("result", outer.get("result", {}))
            proj = {"ts": ts, "port": args.revit_port, "method": "get_project_info", "result": inner}
            save_json(proj_path, proj)
        results["project"] = proj

    if args.what in ("all", "documents"):
        if not args.refresh and cache_valid(docs_path):
            docs = load_json(docs_path)
        else:
            outer = durable_call_via_proxy(args.proxy, args.revit_port, "get_open_documents")
            inner = outer.get("result", {}).get("result", outer.get("result", {}))
            docs = {"ts": ts, "port": args.revit_port, "method": "get_open_documents", "result": inner}
            save_json(docs_path, docs)
        results["documents"] = docs

    print(_json({"ok": True, "outDir": args.out_dir, "ports": args.revit_port, "saved": results}))


if __name__ == "__main__":
    main()
