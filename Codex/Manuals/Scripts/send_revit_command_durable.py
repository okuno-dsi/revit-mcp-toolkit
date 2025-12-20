import requests
import time
import json
import argparse
import os
import sys
from typing import Any, Dict, Tuple, Optional, Mapping

POLLING_INTERVAL_SECONDS = 0.5
MAX_POLLING_ATTEMPTS = 240  # ~120 seconds total
HEADERS = {
    "Content-Type": "application/json; charset=utf-8",
    "Accept-Charset": "utf-8",
    "Accept": "application/json"
}

class RevitMcpError(Exception):
    def __init__(self, where: str, message: str, *, http_status: Optional[int] = None, payload: Optional[dict] = None):
        super().__init__(f"[{where}] {message}")
        self.where = where
        self.http_status = http_status
        self.payload = payload or {}

def _poll_interval(attempt: int) -> float:
    """
    Adaptive polling interval to reduce perceived latency initially
    and server load later. Starts fast, backs off to a small steady rate.
    """
    if attempt < 6:
        return 0.15  # quick first second
    if attempt < 20:
        return 0.5
    if attempt < 100:
        return 1.0
    return 2.0

def _json_or_raise(resp: requests.Response, where: str) -> Any:
    try:
        return resp.json()
    except Exception:
        text = (resp.text or "").strip()
        if resp.status_code >= 400:
            raise RevitMcpError(where, f"HTTP {resp.status_code} {resp.reason}; body={text}", http_status=resp.status_code)
        raise RevitMcpError(where, f"Invalid JSON body; body={text}", http_status=resp.status_code)

def _raise_if_jsonrpc_error(obj: Any, where: str):
    if isinstance(obj, Mapping) and obj.get("error"):
        err = obj["error"]
        if isinstance(err, Mapping):
            code = err.get("code")
            message = err.get("message") or "JSON-RPC error"
            data = err.get("data")
            raise RevitMcpError(where, f"JSON-RPC error code={code} message={message}", payload={"error": err, "data": data})
        raise RevitMcpError(where, f"JSON-RPC error: {err!r}", payload={"error": err})

def _normalize_params(params: Dict[str, Any]) -> Dict[str, Any]:
    """Best-effort normalization to avoid schema mismatches from various callers.
    - Ensure elementIds/uniqueIds are arrays when provided as scalars
    - For smoke_test wrapper (params={method, params}), normalize inner params as well
    """
    def ensure_list(d: Dict[str, Any], key: str):
        if key in d and not isinstance(d[key], (list, tuple)):
            d[key] = [d[key]] if d[key] is not None else []

    if not isinstance(params, dict):
        return params

    ensure_list(params, "elementIds")
    ensure_list(params, "uniqueIds")

    # Normalize common nested payloads like smoke_test { method, params }
    inner = params.get("params")
    if isinstance(inner, dict):
        ensure_list(inner, "elementIds")
        ensure_list(inner, "uniqueIds")
        params["params"] = inner

    return params


def send_request(port: int, method: str, params: Optional[Dict[str, Any]] = None, *, force: bool = False,
                 timeout: Tuple[float, float] = (3.0, 120.0), max_wait_seconds: Optional[float] = None,
                 job_timeout_sec: Optional[int] = None) -> Dict[str, Any]:
    if params is None:
        params = {}
    base = f"http://localhost:{port}"
    enqueue_url = f"{base}/enqueue"
    get_result_url = f"{base}/get_result"
    payload = {"jsonrpc": "2.0", "method": method, "params": _normalize_params(params or {}), "id": int(time.time()*1000)}

    # HTTP keep-alive session
    with requests.Session() as sess:
        sess.headers.update(HEADERS)

        # enqueue
        post_params = {"force": 1} if force else {}
        if job_timeout_sec and job_timeout_sec > 0:
            post_params["timeout"] = int(job_timeout_sec)
        try:
            r = sess.post(enqueue_url, json=payload, params=post_params, timeout=timeout)
        except requests.RequestException as e:
            raise RevitMcpError("enqueue", f"HTTP request failed: {e}")
        if r.status_code >= 400:
            data = _json_or_raise(r, "enqueue")
            _raise_if_jsonrpc_error(data, "enqueue")
            raise RevitMcpError("enqueue", f"HTTP {r.status_code} {r.reason}", http_status=r.status_code, payload=data)
        data = _json_or_raise(r, "enqueue")
        _raise_if_jsonrpc_error(data, "enqueue")
        if isinstance(data, Mapping) and data.get("ok") is False:
            msg = data.get("error") or data.get("msg") or "enqueue failed"
            raise RevitMcpError("enqueue", str(msg), payload=data)

        # immediate path
        if isinstance(data, Mapping) and data.get("ok") is True and "jobId" not in data and "commandId" not in data:
            return data

        job_id = None
        if isinstance(data, Mapping):
            job_id = data.get("jobId") or data.get("job_id")

    # poll via durable job endpoint for reliability
        attempts = 0
        attempts_limit = MAX_POLLING_ATTEMPTS
        if isinstance(max_wait_seconds, (int, float)) and max_wait_seconds > 0:
            try:
                # Approximate attempts based on average 0.5s to keep behavior similar
                attempts_limit = max(1, int(max_wait_seconds / 0.5))
            except Exception:
                attempts_limit = MAX_POLLING_ATTEMPTS

        job_url = f"{base}/job/{job_id}" if job_id else None
        etag: Optional[str] = None
        while attempts < attempts_limit:
            # Build conditional headers
            h = dict(HEADERS)
            if etag:
                h["If-None-Match"] = etag
            try:
                if job_url:
                    gr = sess.get(job_url, headers=h, timeout=timeout)
                else:
                    # fallback to legacy get_result when no jobId was provided (compat)
                    gr = sess.get(get_result_url, headers=h, timeout=timeout)
            except requests.RequestException as e:
                raise RevitMcpError("get_result", f"HTTP request failed: {e}")

            # Suggested backoff from server
            retry_after = gr.headers.get("Retry-After")
            try:
                next_sleep = float(retry_after) if retry_after is not None else _poll_interval(attempts)
            except Exception:
                next_sleep = _poll_interval(attempts)

            if gr.status_code == 304:
                # Not modified; continue with backoff
                attempts += 1
                time.sleep(next_sleep)
                continue
            if gr.status_code in (202, 204):
                attempts += 1
                time.sleep(next_sleep)
                continue
            if gr.status_code >= 400:
                data_err = _json_or_raise(gr, "get_result")
                _raise_if_jsonrpc_error(data_err, "get_result")
                raise RevitMcpError("get_result", f"HTTP {gr.status_code} {gr.reason}", http_status=gr.status_code, payload=data_err)

            # capture ETag for subsequent conditional requests
            etag = gr.headers.get("ETag") or etag

            data_res = _json_or_raise(gr, "get_result")
            # When hitting /job/{id}, the payload is a raw row dict
            if job_url and isinstance(data_res, Mapping) and data_res.get("state"):
                st = data_res.get("state")
                if st == "SUCCEEDED":
                    rjson = data_res.get("result_json")
                    if isinstance(rjson, str) and rjson.strip():
                        try:
                            return json.loads(rjson)
                        except json.JSONDecodeError:
                            return {"ok": True, "result": rjson}
                    return {"ok": True}
                if st in ("FAILED", "TIMEOUT", "DEAD"):
                    msg = data_res.get("error_msg") or st
                    raise RevitMcpError("get_result", str(msg), payload=data_res)
                attempts += 1
                time.sleep(next_sleep)
                continue

            _raise_if_jsonrpc_error(data_res, "get_result")
            if isinstance(data_res, Mapping) and "ok" in data_res:
                if data_res.get("ok") is True:
                    return data_res
                msg = data_res.get("error") or data_res.get("msg") or "Command failed"
                raise RevitMcpError("get_result", str(msg), payload=data_res)
            if isinstance(data_res, Mapping):
                return data_res
            raise RevitMcpError("get_result", f"Unexpected payload shape: {data_res!r}")

        total_wait = attempts * 0.5  # approximate for message
        raise RevitMcpError("get_result", f"Polling timed out for '{method}' after {total_wait:.1f} sec.")

def main():
    parser = argparse.ArgumentParser(description="Send a durable JSON-RPC command to Revit MCP server.")
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--command", type=str, required=True)
    parser.add_argument("--params", type=str, default=None)
    parser.add_argument("--params-file", type=str)
    parser.add_argument("--output-file", type=str)
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--timeout-sec", type=int, default=None, help="Server-side job timeout (seconds) to set on enqueue.")
    parser.add_argument("--wait-seconds", type=float, default=None)
    args = parser.parse_args()

    # load params
    if args.params_file:
        with open(args.params_file, 'r', encoding='utf-8') as f:
            params_dict = json.load(f)
    elif args.params:
        params_dict = json.loads(args.params)
    else:
        params_dict = {}

    try:
        result = send_request(args.port, args.command, params_dict, force=args.force, max_wait_seconds=args.wait_seconds, job_timeout_sec=args.timeout_sec)
        if args.output_file:
            outp = os.path.abspath(args.output_file)
            with open(outp, 'w', encoding='utf-8') as f:
                json.dump(result, f, indent=2, ensure_ascii=False)
            print(json.dumps({"ok": True, "savedTo": outp}, ensure_ascii=False))
        else:
            print(json.dumps(result, indent=2, ensure_ascii=False))
    except RevitMcpError as e:
        err_json = {"ok": False, "where": e.where, "httpStatus": e.http_status, "error": str(e), "payload": e.payload}
        if args.output_file:
            outp = os.path.abspath(args.output_file)
            with open(outp, 'w', encoding='utf-8') as f:
                json.dump(err_json, f, indent=2, ensure_ascii=False)
        print(json.dumps(err_json, indent=2, ensure_ascii=False))
        sys.exit(1)

if __name__ == "__main__":
    main()
