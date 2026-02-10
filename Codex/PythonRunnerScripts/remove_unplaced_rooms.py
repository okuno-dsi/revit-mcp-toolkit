# @feature: 配置されていない/不正な部屋を削除する
# @keywords: 部屋, クリーニング
# -*- coding: utf-8 -*-
"""
現在のプロジェクトから、配置されていない部屋や不正な部屋を削除します。
判定基準（いずれか該当）:
- isPlaced == False
- 面積が 0 または負
- 座標/位置情報が欠落

注意:
- 直接削除します（ログ出力あり）。
- 変更したい場合は DRY_RUN を True にしてください。
"""
import json
import time
import requests
from typing import Any, Dict, List

DEFAULT_PORT = 5210
DEFAULT_TIMEOUT = 30
POLL_INTERVAL = 0.5
POLL_TIMEOUT = 60

DRY_RUN = False


def detect_rpc_endpoint(base_url: str) -> str:
    for ep in ("/rpc", "/jsonrpc"):
        url = f"{base_url}{ep}"
        try:
            requests.post(url, json={"jsonrpc": "2.0", "id": "ping", "method": "noop"}, timeout=3)
            return url
        except Exception:
            continue
    return f"{base_url}/rpc"


def unwrap_result(obj):
    if not isinstance(obj, dict):
        return obj
    if "result" in obj and isinstance(obj["result"], dict):
        inner = obj["result"]
        if "result" in inner and isinstance(inner["result"], dict):
            return inner["result"]
        return inner
    return obj


def poll_job(base_url: str, job_id: str, timeout_sec: int = POLL_TIMEOUT):
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
            return unwrap_result(json.loads(result_json)) if result_json else {"ok": True}
        if state in ("FAILED", "TIMEOUT", "DEAD"):
            raise RuntimeError(job.get("error_msg") or state)
        time.sleep(POLL_INTERVAL)
    raise TimeoutError(f"job polling timed out (jobId={job_id})")


def rpc(base_url: str, method: str, params=None) -> Any:
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


def try_get_rooms(base_url: str) -> List[Dict[str, Any]]:
    """element.get_rooms (paged) を優先。ダメなら get_rooms を試す。"""
    rooms = []
    for method in ("element.get_rooms", "get_rooms"):
        try:
            # try paging
            first = rpc(base_url, method, {"skip": 0, "count": 0})
            total = None
            if isinstance(first, dict):
                total = first.get("totalCount") or first.get("count")
            if total is None:
                # fallback: try without paging
                data = rpc(base_url, method, {})
                if isinstance(data, dict):
                    rooms = data.get("rooms") or data.get("elements") or []
                if rooms:
                    return rooms
                continue
            total = int(total)
            skip = 0
            batch = 200
            while skip < total:
                data = rpc(base_url, method, {"skip": skip, "count": batch})
                if not isinstance(data, dict):
                    break
                chunk = data.get("rooms") or data.get("elements") or []
                if not chunk:
                    break
                rooms.extend(chunk)
                skip += batch
            if rooms:
                return rooms
        except Exception:
            continue
    return rooms


def get_area_value(room: Dict[str, Any]) -> float:
    for key in ("areaM2", "area", "area_m2", "Area", "AREA"):
        if key in room and room[key] is not None:
            try:
                return float(room[key])
            except Exception:
                pass
    # try parameters if present
    params = room.get("parameters") or {}
    if isinstance(params, dict):
        for k in ("Area", "面積"):
            v = params.get(k)
            if v is None:
                continue
            try:
                return float(v)
            except Exception:
                pass
    return None


def has_location(room: Dict[str, Any]) -> bool:
    for key in ("location", "coordinates", "point", "center"):
        if room.get(key) is not None:
            return True
    return False


def is_invalid_room(room: Dict[str, Any]) -> bool:
    if room is None:
        return True
    if room.get("isPlaced") is False:
        return True
    area = get_area_value(room)
    if area is not None and area <= 0.0:
        return True
    if not has_location(room):
        return True
    return False


def main():
    base_url = f"http://127.0.0.1:{DEFAULT_PORT}"
    rooms = try_get_rooms(base_url)
    if not rooms:
        print("部屋が取得できませんでした。")
        return

    invalid = []
    for r in rooms:
        if is_invalid_room(r):
            rid = r.get("id") or r.get("elementId") or r.get("roomId")
            if rid:
                invalid.append(r)

    print(f"rooms_total={len(rooms)} invalid_candidates={len(invalid)}")

    if not invalid:
        print("削除対象はありません。")
        return

    if DRY_RUN:
        print("DRY_RUN=True: 削除は行いません。")
        for r in invalid:
            rid = r.get("id") or r.get("elementId") or r.get("roomId")
            name = r.get("name") or r.get("roomName") or ""
            lvl = r.get("levelName") or r.get("level") or ""
            area = get_area_value(r)
            print(f"- id={rid} name={name} level={lvl} area={area}")
        return

    # delete one by one to avoid batch failure handling
    deleted = 0
    for r in invalid:
        rid = r.get("id") or r.get("elementId") or r.get("roomId")
        if not rid:
            continue
        try:
            resp = rpc(base_url, "element.delete_room", {"elementId": int(rid)})
            if isinstance(resp, dict) and resp.get("ok") is False:
                print(f"[WARN] delete failed id={rid} msg={resp.get('msg')}")
            else:
                deleted += 1
        except Exception as e:
            print(f"[WARN] delete failed id={rid} err={e}")

    print(f"deleted={deleted}")


if __name__ == "__main__":
    main()
