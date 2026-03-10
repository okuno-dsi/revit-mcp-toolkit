# @feature: 各階の部屋番号・名前・面積をDurable取得して表示 | keywords: 部屋, レベル, 面積, Durable
# -*- coding: utf-8 -*-
"""
Python Script Runner 用:
各階の部屋の「番号 / 名前 / 面積」を Durable で取得して表示する。

使い方:
- 引数なしで実行可（既定ポート 5210）
- 必要に応じて: --port 5210
"""

import argparse
import json
import time
from typing import Any, Dict, List, Optional, Tuple

from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen


DEFAULT_PORT = 5210
REQUEST_TIMEOUT = 120.0
POLL_INTERVAL_SEC = 0.5
POLL_TIMEOUT_SEC = 120.0


def _unwrap(obj: Any) -> Any:
    cur = obj
    for _ in range(3):
        if isinstance(cur, dict) and "result" in cur:
            cur = cur.get("result")
        else:
            break
    return cur


def _post_json(url: str, payload: Dict[str, Any]) -> Dict[str, Any]:
    body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    req = Request(
        url=url,
        data=body,
        method="POST",
        headers={
            "Content-Type": "application/json; charset=utf-8",
            "Accept": "application/json",
        },
    )
    try:
        with urlopen(req, timeout=REQUEST_TIMEOUT) as resp:
            raw = resp.read().decode("utf-8", errors="replace")
            data = json.loads(raw) if raw.strip() else {}
    except HTTPError as e:
        detail = ""
        try:
            detail = e.read().decode("utf-8", errors="replace")
        except Exception:
            pass
        raise RuntimeError(f"HTTP {e.code} on POST {url}: {detail}")
    except URLError as e:
        raise RuntimeError(f"URL error on POST {url}: {e}")

    if isinstance(data, dict) and data.get("error") is not None:
        raise RuntimeError(f"JSON-RPC error: {data.get('error')}")
    return data if isinstance(data, dict) else {"result": data}


def _get_json_with_status(url: str) -> Tuple[int, Dict[str, Any]]:
    req = Request(url=url, method="GET", headers={"Accept": "application/json"})
    try:
        with urlopen(req, timeout=REQUEST_TIMEOUT) as resp:
            status = int(resp.getcode() or 200)
            raw = resp.read().decode("utf-8", errors="replace")
            data = json.loads(raw) if raw.strip() else {}
            if not isinstance(data, dict):
                data = {"result": data}
            return status, data
    except HTTPError as e:
        if e.code in (202, 204):
            return int(e.code), {}
        detail = ""
        try:
            detail = e.read().decode("utf-8", errors="replace")
        except Exception:
            pass
        raise RuntimeError(f"HTTP {e.code} on GET {url}: {detail}")
    except URLError as e:
        raise RuntimeError(f"URL error on GET {url}: {e}")


def _poll_job(base_url: str, job_id: str) -> Any:
    deadline = time.time() + POLL_TIMEOUT_SEC
    url = f"{base_url}/job/{job_id}"
    while time.time() < deadline:
        status, row = _get_json_with_status(url)
        if status in (202, 204):
            time.sleep(POLL_INTERVAL_SEC)
            continue
        state = str(row.get("state") or "").upper()
        if state == "SUCCEEDED":
            result_json = row.get("result_json")
            if isinstance(result_json, str) and result_json.strip():
                try:
                    return _unwrap(json.loads(result_json))
                except Exception:
                    return {"ok": True, "result": result_json}
            return {"ok": True}
        if state in ("FAILED", "TIMEOUT", "DEAD"):
            raise RuntimeError(str(row.get("error_msg") or state))
        time.sleep(POLL_INTERVAL_SEC)
    raise TimeoutError(f"Job polling timed out: jobId={job_id}")


def _rpc_durable(base_url: str, method: str, params: Optional[Dict[str, Any]] = None) -> Any:
    payload = {
        "jsonrpc": "2.0",
        "id": f"req-{int(time.time() * 1000)}",
        "method": method,
        "params": params or {},
    }

    # 1) Durable enqueue path (preferred)
    try:
        enq = _post_json(f"{base_url}/enqueue", payload)
        if enq.get("ok") is False:
            raise RuntimeError(str(enq.get("error") or enq.get("msg") or "enqueue failed"))
        job_id = enq.get("jobId") or enq.get("job_id")
        if isinstance(job_id, str) and job_id:
            return _poll_job(base_url, job_id)
        return _unwrap(enq)
    except Exception:
        pass

    # 2) Fallback /rpc (queued 対応)
    rpc = _post_json(f"{base_url}/rpc", payload)
    result = rpc.get("result")
    if isinstance(result, dict) and result.get("queued"):
        job_id = result.get("jobId") or result.get("job_id")
        if not job_id:
            raise RuntimeError("queued=true but jobId missing")
        return _poll_job(base_url, str(job_id))
    return _unwrap(rpc)


def _safe_str(v: Any) -> str:
    return "" if v is None else str(v).strip()


def _as_rooms(payload: Any) -> List[Dict[str, Any]]:
    if isinstance(payload, dict):
        rooms = payload.get("rooms")
        if isinstance(rooms, list):
            return [r for r in rooms if isinstance(r, dict)]
    return []


def _collect_all_rooms(base_url: str, method: str, page_size: int = 200) -> List[Dict[str, Any]]:
    rooms: List[Dict[str, Any]] = []
    skip = 0
    max_pages = 200

    for _ in range(max_pages):
        payload = _rpc_durable(base_url, method, {"skip": skip, "count": page_size})
        batch = _as_rooms(payload)
        if not batch:
            break
        rooms.extend(batch)
        got = len(batch)
        if got < page_size:
            break
        skip += got
    return rooms


def _extract_number_area_from_params(params_payload: Any) -> Tuple[str, str]:
    number = ""
    area = ""
    if not isinstance(params_payload, dict):
        return number, area

    params = params_payload.get("parameters")
    if not isinstance(params, list):
        return number, area

    for p in params:
        if not isinstance(p, dict):
            continue
        name = _safe_str(p.get("name"))
        display = _safe_str(p.get("display"))
        value = p.get("value")
        raw = p.get("raw")
        candidate = display or _safe_str(value) or _safe_str(raw)
        if not number and name in ("番号", "Number"):
            number = candidate
        if not area and name in ("面積", "Area", "部屋面積"):
            area = candidate
    return number, area


def main() -> int:
    ap = argparse.ArgumentParser(description="Print rooms by level: number, name, area (durable).")
    ap.add_argument("--port", type=int, default=DEFAULT_PORT, help="Revit MCP port (default: 5210)")
    args = ap.parse_args()

    base_url = f"http://127.0.0.1:{int(args.port)}"

    # 優先: get_rooms, fallback: element.get_rooms (ページングで全件)
    rooms: List[Dict[str, Any]] = []
    last_err = None
    for method in ("get_rooms", "element.get_rooms"):
        try:
            rooms = _collect_all_rooms(base_url, method, page_size=200)
            if rooms:
                break
        except Exception as e:
            last_err = e

    if not rooms:
        if last_err is not None:
            print(f"部屋取得に失敗しました: {last_err}")
            return 1
        print("部屋が取得できませんでした。")
        return 1

    rows: List[Dict[str, str]] = []
    for r in rooms:
        rid = r.get("elementId")
        level = _safe_str(r.get("level")) or "(No Level)"
        name = _safe_str(r.get("name"))
        number = _safe_str(r.get("number"))
        area = _safe_str(r.get("area"))

        # number / area が空なら必要時のみ詳細取得
        if (not number or not area) and isinstance(rid, int):
            for method in ("get_room_params", "element.get_room_params"):
                try:
                    prm = _rpc_durable(base_url, method, {"roomId": rid, "skip": 0, "count": 400})
                    num2, area2 = _extract_number_area_from_params(prm)
                    if not number and num2:
                        number = num2
                    if not area and area2:
                        area = area2
                    if number and area:
                        break
                except Exception:
                    continue

        rows.append(
            {
                "level": level,
                "number": number,
                "name": name,
                "area": area,
            }
        )

    rows.sort(key=lambda x: (x["level"], x["number"], x["name"]))

    current_level = None
    printed = 0
    for row in rows:
        if row["level"] != current_level:
            current_level = row["level"]
            if printed > 0:
                print("")
            print(f"[{current_level}]")
            print("番号\t名前\t面積")
        print(f"{row['number']}\t{row['name']}\t{row['area']}")
        printed += 1

    print("")
    print(f"totalRooms={len(rows)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
