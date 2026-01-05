# proxy_mcp_logger.py
# Python 3.9+ / 依存: 標準ライブラリのみ
import argparse, json, os, sys, datetime, time
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler
from urllib.parse import urlparse
from urllib.request import Request, urlopen
from urllib.error import HTTPError, URLError

def iso_now():
    return datetime.datetime.now().isoformat(timespec="milliseconds")

class TeeProxyHandler(BaseHTTPRequestHandler):
    # これらは起動時にセット
    upstream = "http://127.0.0.1:5209"
    logdir = "logs"

    def _log_jsonl(self, record: dict):
        os.makedirs(self.logdir, exist_ok=True)
        fname = os.path.join(self.logdir, f"{datetime.date.today().isoformat()}_mcp.jsonl")
        with open(fname, "a", encoding="utf-8") as f:
            f.write(json.dumps(record, ensure_ascii=False) + "\n")

    def _forward(self, method: str, path: str, body: bytes, headers: dict):
        url = self.upstream + path
        req_headers = {k: v for k, v in headers.items()
                       if k.lower() not in ("host", "content-length", "accept-encoding", "connection")}
        if body:
            req_headers["Content-Length"] = str(len(body))
        req = Request(url=url, data=body if method != "GET" else None, headers=req_headers, method=method)
        try:
            with urlopen(req, timeout=600) as resp:
                resp_body = resp.read()
                status = getattr(resp, "status", 200)
                resp_headers = dict(resp.headers.items())
                return status, resp_headers, resp_body, None
        except HTTPError as e:
            return e.code, dict(e.headers.items()) if e.headers else {}, e.read() if e.fp else b"", f"HTTPError: {e}"
        except URLError as e:
            return 502, {}, b"", f"URLError: {e}"
        except Exception as e:
            return 500, {}, b"", f"Exception: {e}"

    def _handle(self):
        t0 = time.perf_counter()
        length = int(self.headers.get("Content-Length", 0))
        body = self.rfile.read(length) if length > 0 else b""
        path = self.path
        method = self.command

        # リクエストの JSON を（可能なら）パースしておく
        req_json = None
        if body and "application/json" in self.headers.get("Content-Type",""):
            try:
                req_json = json.loads(body.decode("utf-8"))
            except Exception:
                req_json = {"_parse_error": True}

        status, resp_headers, resp_body, err = self._forward(method, path, body, dict(self.headers))

        # レスポンス JSON（可能なら）
        res_json = None
        ctype = resp_headers.get("Content-Type", "")
        if resp_body and "application/json" in ctype:
            try:
                res_json = json.loads(resp_body.decode("utf-8"))
            except Exception:
                res_json = {"_parse_error": True}

        dt_ms = int((time.perf_counter() - t0)*1000)
        record = {
            "ts": iso_now(),
            "method": method,
            "path": path,
            "req": req_json if req_json is not None else {"_binary_or_non_json": True},
            "res": res_json if res_json is not None else {"_binary_or_non_json": True},
            "status": status,
            "latency_ms": dt_ms,
            "error": err
        }
        self._log_jsonl(record)

        # クライアントへ返却
        self.send_response(status)
        # ヘッダ（Hop-by-Hop を除去）
        hop_by_hop = {"connection", "keep-alive", "proxy-authenticate", "proxy-authorization",
                      "te", "trailers", "transfer-encoding", "upgrade"}
        for k, v in resp_headers.items():
            if k.lower() not in hop_by_hop:
                # Content-Length は自動付与される
                if k.lower() != "content-length":
                    self.send_header(k, v)
        # Ensure Content-Length for client responses
        self.send_header("Content-Length", str(len(resp_body or b"")))
        self.end_headers()
        if resp_body:
            self.wfile.write(resp_body)

    def do_POST(self): self._handle()
    def do_GET(self):  self._handle()

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--listen", type=str, default="127.0.0.1:5210", help="host:port to listen")
    ap.add_argument("--upstream", type=str, default="http://127.0.0.1:5209", help="RevitMCP upstream URL")
    ap.add_argument("--logdir", type=str, default="logs", help="log directory")
    args = ap.parse_args()

    host, port = args.listen.split(":")
    port = int(port)

    TeeProxyHandler.upstream = args.upstream.rstrip("/")
    TeeProxyHandler.logdir = args.logdir

    httpd = ThreadingHTTPServer((host, port), TeeProxyHandler)
    print(f"[proxy] listening on http://{host}:{port} -> upstream {TeeProxyHandler.upstream}")
    httpd.serve_forever()

if __name__ == "__main__":
    main()
