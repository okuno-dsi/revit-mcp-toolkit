import time
from typing import Any, Dict, Iterable, List, Optional, Tuple
import importlib.util as _iu
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def _resolve_send_revit_command_path() -> Path:
    candidates = [
        ROOT / "send_revit_command.py",
        # Default tool location for local LLM + MCP utilities
        ROOT.parents[1] / "NVIDIA-Nemotron-v3" / "tool" / "send_revit_command.py",
    ]
    for p in candidates:
        try:
            if p.exists():
                return p
        except Exception:
            continue
    raise SystemExit(f"send_revit_command.py not found. Tried: {[str(p) for p in candidates]}")


SEND_PATH = _resolve_send_revit_command_path()
spec = _iu.spec_from_file_location("send_revit_command", SEND_PATH)
if spec is None or spec.loader is None:
    raise SystemExit(f"Failed to load send_revit_command.py from: {SEND_PATH}")
send_revit_command = _iu.module_from_spec(spec)  # type: ignore
spec.loader.exec_module(send_revit_command)  # type: ignore


class McpBusy(Exception):
    pass


def _looks_busy(payload: Dict[str, Any]) -> bool:
    if not isinstance(payload, dict):
        return False
    if payload.get("httpStatus") == 409:
        return True
    code = (payload.get("code") or "").upper()
    if code in {"REQUEST_IN_PROGRESS", "HTTP_409"}:
        return True
    msg = str(payload.get("msg") or payload.get("error") or "").lower()
    if "in progress" in msg or "busy" in msg:
        return True
    return False


def _looks_timeout(err: BaseException, payload: Optional[Dict[str, Any]]) -> bool:
    txt = str(err)
    if any(s in txt for s in ["Execution did not complete", "timed out", "heartbeat lost"]):
        return True
    if isinstance(payload, dict):
        code = (payload.get("code") or "").upper()
        if code in {"EXECUTION_TIMEOUT"}:
            return True
        em = str(payload.get("error") or payload.get("msg") or "")
        if any(s in em for s in ["timed out", "heartbeat lost"]):
            return True
    return False


def call_mcp(
    port: int,
    method: str,
    params: Optional[Dict[str, Any]] = None,
    *,
    retries: int = 5,
    base_wait: float = 1.0,
    max_wait_seconds: Optional[float] = 90.0,
    force_on_retry: bool = False,
) -> Dict[str, Any]:
    """Resilient MCP call with backoff for 409/busy and timeouts."""
    if params is None:
        params = {}
    attempt = 0
    last_err: Optional[Exception] = None
    while attempt <= retries:
        try:
            # On retries, optionally set force and increase wait
            force = force_on_retry and attempt > 0
            wait = None if attempt == 0 else min((max_wait_seconds or 120.0) * 1.5, 180.0)
            res = send_revit_command.send_revit_request(
                port, method, params, force=force, max_wait_seconds=wait or max_wait_seconds
            )
            # Some APIs return structured error inside payload
            top = res.get("result") or res
            if isinstance(top, dict) and top.get("ok") is False:
                if _looks_busy(top):
                    raise McpBusy(str(top))
            return res
        except send_revit_command.RevitMcpError as e:  # type: ignore[attr-defined]
            last_err = e
            if _looks_busy(e.payload):
                # Backoff and retry
                time.sleep(base_wait * (2 ** attempt))
                attempt += 1
                continue
            if _looks_timeout(e, e.payload):
                time.sleep(base_wait * (2 ** attempt))
                attempt += 1
                continue
            raise
        except McpBusy:
            time.sleep(base_wait * (2 ** attempt))
            attempt += 1
            continue
    # Exhausted retries
    if last_err:
        raise last_err
    raise RuntimeError("MCP call failed without exception but without result as well")


def chunked(iterable: Iterable[Any], size: int) -> Iterable[List[Any]]:
    buf: List[Any] = []
    for x in iterable:
        buf.append(x)
        if len(buf) >= size:
            yield buf
            buf = []
    if buf:
        yield buf


def get_element_info_safe(
    port: int,
    element_ids: List[int],
    *,
    rich: bool = True,
    batch_size: int = 8,
) -> Dict[str, Any]:
    """Fetch element info in small batches with retries and merge results."""
    all_elements: List[Dict[str, Any]] = []
    for batch in chunked(element_ids, batch_size):
        payload = {"elementIds": batch, "rich": bool(rich)}
        res = call_mcp(port, "get_element_info", payload, retries=4, base_wait=1.0, max_wait_seconds=90.0)
        top = res.get("result") or res
        if isinstance(top, dict) and "result" in top:
            top = top["result"]
        part = list((top or {}).get("elements", []))
        all_elements.extend(part)
    return {"ok": True, "elements": all_elements}
