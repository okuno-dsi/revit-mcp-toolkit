# @feature: 移動経路(PathOfTravel)作成 | keywords: 経路, 避難, PathOfTravel, egress
# -*- coding: utf-8 -*-
"""
選択した 2 つのドア間で PathOfTravel を作成します（Revit API）。
- egress.create_waypoint_guided_path を使用（mode=pointToDoor）
- 1つ目の選択ドア → start 点（座標: mm）
- 2つ目の選択ドア → targetDoorId

使い方:
  python path_create_from_two_selected_doors.py --port 5210
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
    """ /rpc → /jsonrpc の順で有効なエンドポイントを探す """
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


def get_context(client: RpcClient) -> dict:
    ctx = client.call("help.get_context", {"includeSelectionIds": True, "maxSelectionIds": 10})
    data = ctx.get("data") if isinstance(ctx, dict) else None
    return data if isinstance(data, dict) else {}


def get_selected_ids(ctx_data: dict) -> list:
    return ctx_data.get("selectionIds") or []


def get_element_info(client: RpcClient, element_id: int) -> dict:
    info = client.call("element.get_element_info", {"elementIds": [element_id]})
    if not isinstance(info, dict):
        return {}
    elems = info.get("elements") or []
    return elems[0] if elems else {}


def make_candidates(pt: dict, offsets_mm=None) -> list:
    if offsets_mm is None:
        offsets_mm = [0, 150, 300, 450]
    x = pt.get("x", 0)
    y = pt.get("y", 0)
    z = pt.get("z", 0)
    cands = [{"x": x, "y": y, "z": z}]
    for d in offsets_mm:
        if d == 0:
            continue
        cands.append({"x": x + d, "y": y, "z": z})
        cands.append({"x": x - d, "y": y, "z": z})
        cands.append({"x": x, "y": y + d, "z": z})
        cands.append({"x": x, "y": y - d, "z": z})
    return cands


def find_best_start(client: RpcClient, view_id: int, start: dict, end: dict) -> dict:
    starts = make_candidates(start)
    ends = [end for _ in starts]
    params = {"starts": starts, "ends": ends}
    if view_id:
        params["viewId"] = view_id
    try:
        res = client.call("route.find_shortest_paths", params)
    except Exception:
        return start
    if not isinstance(res, dict) or not res.get("ok"):
        return start
    data = res.get("data") or {}
    best_idx = data.get("bestIndex")
    if isinstance(best_idx, int) and 0 <= best_idx < len(starts):
        return starts[best_idx]
    return start


def find_best_pair_by_route(client: RpcClient, view_id: int, start: dict, end: dict) -> tuple:
    starts = make_candidates(start)
    ends = make_candidates(end)
    pairs_start = []
    pairs_end = []
    for s in starts:
        for e in ends:
            pairs_start.append(s)
            pairs_end.append(e)
    params = {"starts": pairs_start, "ends": pairs_end}
    if view_id:
        params["viewId"] = view_id
    try:
        res = client.call("route.find_shortest_paths", params)
    except Exception:
        return start, end
    if not isinstance(res, dict) or not res.get("ok"):
        return start, end
    data = res.get("data") or {}
    best_idx = data.get("bestIndex")
    if isinstance(best_idx, int) and 0 <= best_idx < len(pairs_start):
        return pairs_start[best_idx], pairs_end[best_idx]
    return start, end


def run_egress(client: RpcClient, params: dict) -> dict:
    try:
        return client.call("egress.create_waypoint_guided_path", params)
    except Exception as e:
        return {"ok": False, "code": "EXCEPTION", "msg": str(e)}


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=5210)
    ap.add_argument("--clearance-mm", type=float, default=200.0, help="PathOfTravel clearance (mm)")
    ap.add_argument("--door-approach-offset-mm", type=float, default=None, help="Door approach offset (mm)")
    # Python Runner では引数指定ができないため、既定で有効にする
    ap.add_argument("--try-swap", action="store_true", default=True, help="NO_PATH の場合に start/target を入れ替えて再試行")
    ap.add_argument("--use-waypoints-from-route", action="store_true", default=True, help="route.find_shortest_paths の点列をウェイポイントに利用")
    ap.add_argument("--waypoint-step", type=int, default=4, help="ウェイポイント間引き（N点ごと）")
    ap.add_argument("--create-label", action="store_true", help="距離ラベルを作成")
    ap.add_argument("--create-tag", action="store_true", help="タグを作成（要タグタイプ）")
    args = ap.parse_args()

    base_url = f"http://127.0.0.1:{args.port}"
    client = RpcClient(base_url)

    ctx_data = get_context(client)
    ids = get_selected_ids(ctx_data)
    if len(ids) < 2:
        print("選択要素が2つ必要です（ドアを2つ選択してください）。")
        return 1

    a_id, b_id = ids[0], ids[1]
    a = get_element_info(client, a_id)
    b = get_element_info(client, b_id)

    # 選択内容の確認出力
    def _cat(e: dict) -> str:
        return (e.get("category") or e.get("categoryName") or "").strip()

    def _name(e: dict) -> str:
        return (e.get("typeName") or e.get("familyName") or e.get("name") or "").strip()

    print("=== Selected Elements ===")
    print(f"- id={a_id} | category={_cat(a)} | name={_name(a)}")
    print(f"- id={b_id} | category={_cat(b)} | name={_name(b)}")

    for e_id, e in ((a_id, a), (b_id, b)):
        cat = _cat(e)
        if cat and ("ドア" not in cat and "Door" not in cat and "Doors" not in cat):
            print(f"注意: id={e_id} はドアカテゴリに見えません（category={cat}）。")

    start = (a.get("coordinatesMm") or {})
    if not start:
        print("開始点の座標が取得できません。")
        return 1
    end = (b.get("coordinatesMm") or {})
    if not end:
        print("終了点の座標が取得できません。")
        return 1

    # ViewPlan 以外だと NO_PATH になりやすい
    raw_view_type = (ctx_data.get("rawActiveViewType") or "").lower()
    if "plan" not in raw_view_type:
        print("注意: 平面ビュー（ViewPlan）で実行してください。現在のビュー種別:", ctx_data.get("rawActiveViewType"))

    def build_params(start_pt: dict, target_id: int, waypoints=None, clearance_mm: float = 200.0, door_offset_mm: Optional[float] = None) -> dict:
        p = {
            "mode": "pointToDoor",
            "start": {"x": start_pt.get("x", 0), "y": start_pt.get("y", 0), "z": start_pt.get("z", 0)},
            "targetDoorId": target_id,
            "clearanceMm": float(clearance_mm),
            "createLabel": bool(args.create_label),
            "createTag": bool(args.create_tag),
            "tagTypeFromSelection": True,
        }
        if door_offset_mm is not None:
            p["doorApproachOffsetMm"] = float(door_offset_mm)
        if waypoints:
            p["waypoints"] = waypoints
        return p

    # 位置が壁内にあると NO_PATH になりやすいので、周辺候補から最短を選ぶ
    view_id = ctx_data.get("activeViewId") or 0
    start_best = find_best_start(client, view_id, start, end)

    # ルート解析（候補点 -> 終点）からウェイポイントを作る（任意）
    waypoints = None
    if args.use_waypoints_from_route:
        try:
            route = client.call(
                "route.find_shortest_paths",
                {"start": start_best, "end": end, "viewId": view_id} if view_id else {"start": start_best, "end": end},
            )
            data = (route or {}).get("data") or {}
            items = data.get("items") or []
            if items:
                pts = items[0].get("points") or []
                if pts and len(pts) > 2:
                    step = max(2, int(args.waypoint_step))
                    waypoints = pts[1:-1:step]
        except Exception:
            waypoints = None

    params = build_params(start_best, b_id, waypoints, clearance_mm=args.clearance_mm,
                          door_offset_mm=args.door_approach_offset_mm)
    result = run_egress(client, params)
    # NO_PATH の場合は、候補点の組み合わせ＆クリアランス縮小で再試行
    if isinstance(result, dict) and result.get("code") == "NO_PATH":
        alt_start, alt_end = find_best_pair_by_route(client, view_id, start_best, end)
        for cm in (args.clearance_mm, 150.0, 100.0, 50.0):
            params_retry = build_params(alt_start, b_id, waypoints=None, clearance_mm=cm, door_offset_mm=min(cm, 150.0))
            result = run_egress(client, params_retry)
            if isinstance(result, dict) and result.get("ok"):
                break
    if not (isinstance(result, dict) and result.get("ok")) and args.try_swap:
        # start/target を入れ替えて再試行
        swapped_start = find_best_start(client, view_id, end, start)
        waypoints2 = None
        if args.use_waypoints_from_route:
            try:
                route2 = client.call(
                    "route.find_shortest_paths",
                    {"start": swapped_start, "end": start, "viewId": view_id} if view_id else {"start": swapped_start, "end": start},
                )
                data2 = (route2 or {}).get("data") or {}
                items2 = data2.get("items") or []
                if items2:
                    pts2 = items2[0].get("points") or []
                    if pts2 and len(pts2) > 2:
                        step = max(2, int(args.waypoint_step))
                        waypoints2 = pts2[1:-1:step]
            except Exception:
                waypoints2 = None
        params2 = build_params(swapped_start, a_id, waypoints2, clearance_mm=args.clearance_mm,
                               door_offset_mm=args.door_approach_offset_mm)
        result = run_egress(client, params2)

    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
