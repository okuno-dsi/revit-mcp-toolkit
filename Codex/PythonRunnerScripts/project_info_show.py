# -*- coding: utf-8 -*-
# @feature: project info show | keywords: misc
import json
import os
import time
import requests

PORT = int(os.environ.get("REVIT_MCP_PORT", "5210"))
BASE = f"http://127.0.0.1:{PORT}"

def enqueue(method, params=None):
    payload = {
        "jsonrpc": "2.0",
        "id": int(time.time() * 1000),
        "method": method,
        "params": params or {}
    }
    r = requests.post(f"{BASE}/enqueue", json=payload, timeout=10)
    r.raise_for_status()
    return r.json()

def poll_job(job_id, timeout=60):
    url = f"{BASE}/job/{job_id}"
    t0 = time.time()
    while time.time() - t0 < timeout:
        r = requests.get(url, timeout=10)
        if r.status_code in (202, 204):
            time.sleep(0.5)
            continue
        r.raise_for_status()
        row = r.json()
        state = row.get("state")
        if state == "SUCCEEDED":
            result_json = row.get("result_json")
            return json.loads(result_json) if result_json else {}
        if state in ("FAILED", "TIMEOUT", "DEAD"):
            raise RuntimeError(row.get("error_msg") or state)
        time.sleep(0.5)
    raise TimeoutError(f"job timeout: {job_id}")

def unwrap(obj):
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict) and "data" in obj:
        obj = obj["data"]
    return obj

def call(method):
    first = enqueue(method)
    if isinstance(first, dict) and "result" in first:
        first = first["result"]
    job_id = first.get("jobId") or first.get("job_id")
    res = poll_job(job_id) if job_id else first
    return unwrap(res)

def show(pi):
    if isinstance(pi, dict) and pi.get("ok") is False:
        print("Failed:", pi.get("msg"))
        return
    def g(k):
        v = pi.get(k) if isinstance(pi, dict) else None
        return v if v not in (None, "") else "-"
    print("=== Revit Project Info ===")
    print("projectName   :", g("projectName"))
    print("projectNumber :", g("projectNumber"))
    print("clientName    :", g("clientName"))
    print("status        :", g("status"))
    print("issueDate     :", g("issueDate"))
    print("address       :", g("address"))
    print("elementId     :", g("elementId"))
    print("uniqueId      :", g("uniqueId"))
    params = pi.get("parameters") if isinstance(pi, dict) else None
    print("parameters    :", len(params or []), "items")

if __name__ == "__main__":
    show(call("doc.get_project_info"))
