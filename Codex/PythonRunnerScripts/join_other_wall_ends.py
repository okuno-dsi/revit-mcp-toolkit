# @feature: join other wall ends | keywords: 壁, ビュー
import json
import math
from typing import Any, Dict, List, Tuple

from send_revit_command_durable import send_request, RevitMcpError

PORT = 5210


def _unwrap(result: Dict[str, Any]) -> Dict[str, Any]:
    return (result.get("result") or {}).get("result") or {}


def _dist_mm(a: Dict[str, float], b: Dict[str, float]) -> float:
    dx = a["x"] - b["x"]
    dy = a["y"] - b["y"]
    dz = a.get("z", 0.0) - b.get("z", 0.0)
    return math.sqrt(dx * dx + dy * dy + dz * dz)


def _dist_point_to_segment_mm(p: Dict[str, float], a: Dict[str, float], b: Dict[str, float]) -> float:
    """点 p と線分 a-b の最短距離（mm）"""
    ax, ay, az = a["x"], a["y"], a.get("z", 0.0)
    bx, by, bz = b["x"], b["y"], b.get("z", 0.0)
    px, py, pz = p["x"], p["y"], p.get("z", 0.0)

    vx, vy, vz = bx - ax, by - ay, bz - az
    wx, wy, wz = px - ax, py - ay, pz - az

    vv = vx * vx + vy * vy + vz * vz
    if vv <= 1e-9:
        # a と b が同一点（退化）なら距離は点同士
        return _dist_mm(p, a)

    t = (wx * vx + wy * vy + wz * vz) / vv
    if t < 0.0:
        cx, cy, cz = ax, ay, az
    elif t > 1.0:
        cx, cy, cz = bx, by, bz
    else:
        cx, cy, cz = ax + t * vx, ay + t * vy, az + t * vz

    dx, dy, dz = px - cx, py - cy, pz - cz
    return math.sqrt(dx * dx + dy * dy + dz * dz)


def main() -> None:
    # 選択中の 2 枚の壁について、交点とは反対側の端点が
    # 「他の壁の基準線セグメント（壁厚範囲内）」に当たっている場合は、その壁同士を join_elements で結合します。
    try:
        # 1) 選択中の要素 ID を取得
        res_sel = send_request(PORT, "get_selected_element_ids", {})
        sel_payload = _unwrap(res_sel)
        if not sel_payload.get("ok"):
            raise RevitMcpError("get_selected_element_ids", f"result not ok: {sel_payload!r}")
        sel_ids: List[int] = sel_payload.get("elementIds") or []
        if len(sel_ids) != 2:
            print(json.dumps({"ok": False, "msg": f"Expecting exactly 2 selected elements, got {len(sel_ids)}."}, ensure_ascii=False))
            return

        wall_ids = sel_ids

        # 2) 現在ビュー ID を取得
        res_view = send_request(PORT, "get_current_view", {})
        view_payload = _unwrap(res_view)
        if not view_payload.get("ok"):
            raise RevitMcpError("get_current_view", f"result not ok: {view_payload!r}")
        view_id = int(view_payload["viewId"])

        # 3) ビュー内の全壁を取得（start/end/thickness mm 付き）
        res_walls = send_request(PORT, "get_walls", {"viewId": view_id})
        walls_payload = _unwrap(res_walls)
        if not walls_payload.get("ok"):
            raise RevitMcpError("get_walls", f"result not ok: {walls_payload!r}")
        walls = walls_payload.get("walls") or []

        # 4) 対象 2 枚の壁の基線（両端座標）を取得
        endpoints: Dict[int, Tuple[Dict[str, float], Dict[str, float]]] = {}
        for wid in wall_ids:
            res_base = send_request(PORT, "get_wall_baseline", {"elementId": wid})
            base_payload = _unwrap(res_base)
            if not base_payload.get("ok"):
                raise RevitMcpError("get_wall_baseline", f"result not ok for {wid}: {base_payload!r}")
            bl = base_payload["baseline"]
            endpoints[wid] = (bl["start"], bl["end"])

        # 5) 2 枚の壁で共通している端部（交点側）を特定
        a0, a1 = endpoints[wall_ids[0]]
        b0, b1 = endpoints[wall_ids[1]]
        tol_endpoint = 1.0  # mm

        common_pairs = []
        for ai, a_pt in enumerate((a0, a1)):
            for bi, b_pt in enumerate((b0, b1)):
                if _dist_mm(a_pt, b_pt) < tol_endpoint:
                    common_pairs.append((ai, bi))

        if not common_pairs:
            print(json.dumps({"ok": False, "msg": "Two walls do not share a common endpoint; cannot infer 'other ends'."}, ensure_ascii=False))
            return

        # 共通端は 1 箇所と仮定し、残りを他端とする
        common_a_idx, common_b_idx = common_pairs[0]
        other_a_idx = 1 - common_a_idx
        other_b_idx = 1 - common_b_idx

        other_ends = [
            (wall_ids[0], endpoints[wall_ids[0]][other_a_idx]),
            (wall_ids[1], endpoints[wall_ids[1]][other_b_idx]),
        ]

        # 6) 他端ごとに、ビュー内の他の壁の「基準線セグメント」との距離が
        #    壁厚/2 + 5mm 以内のものを探して join_elements
        joins_done = []
        for wid, pt in other_ends:
            best_id = None
            best_dist = None

            for w in walls:
                cid = int(w["elementId"])
                if cid == wid:
                    continue
                s = w.get("start")
                e = w.get("end")
                if not s or not e:
                    continue

                thickness = float(w.get("thickness") or 0.0)
                # 壁厚が取得できない場合は 300mm 程度を仮置き
                if thickness <= 0.0:
                    thickness = 300.0

                d_seg = _dist_point_to_segment_mm(pt, s, e)
                # 壁厚の半分 + 少し余裕
                threshold = thickness * 0.5 + 5.0

                if d_seg <= threshold and (best_dist is None or d_seg < best_dist):
                    best_dist = d_seg
                    best_id = cid

            if best_id is not None and best_dist is not None:
                try:
                    send_request(PORT, "join_elements", {"elementIdA": wid, "elementIdB": best_id})
                    joins_done.append({"a": wid, "b": best_id, "distanceMm": best_dist})
                except RevitMcpError as e:
                    joins_done.append({"a": wid, "b": best_id, "error": str(e)})

        print(
            json.dumps(
                {
                    "ok": True,
                    "selectedWalls": wall_ids,
                    "joins": joins_done,
                    "note": "Endpoints whose distance to another wall baseline segment is within (thickness/2 + 5mm) were joined via join_elements."
                },
                ensure_ascii=False,
                indent=2,
            )
        )
    except RevitMcpError as e:
        print(
            json.dumps(
                {
                    "ok": False,
                    "where": e.where,
                    "error": str(e),
                    "payload": e.payload,
                },
                ensure_ascii=False,
                indent=2,
            )
        )


if __name__ == "__main__":
    main()

