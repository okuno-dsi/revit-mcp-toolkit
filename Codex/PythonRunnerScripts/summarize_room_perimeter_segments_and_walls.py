# @feature: summarize room perimeter segments and walls | keywords: 柱, 壁, 部屋, ビュー, 集計表
import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, List, Tuple


def _add_scripts_to_path() -> None:
    here = Path(__file__).resolve().parent
    if str(here) not in sys.path:
        sys.path.insert(0, str(here))


_add_scripts_to_path()

from send_revit_command_durable import send_request, RevitMcpError  # type: ignore  # noqa: E402


def unwrap(payload: Dict[str, Any]) -> Dict[str, Any]:
    """
    send_request(...) の JSON-RPC ラッパーを剥いで、
    Revit MCP コマンドの実結果 { ok, ... } を返す。
    """
    obj: Any = payload
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict):
        return obj
    return {}


def ensure_room_selected(port: int) -> Tuple[int, int]:
    sel_env = send_request(port, "get_selected_element_ids", {})
    sel = unwrap(sel_env)
    ids = sel.get("elementIds") or []
    if not ids:
        raise SystemExit(
            json.dumps(
                {
                    "ok": False,
                    "code": "NO_SELECTION",
                    "msg": "Room を1つ選択してから実行してください。",
                },
                ensure_ascii=False,
            )
        )
    room_id = int(ids[0])
    active_view_id = int(sel.get("activeViewId") or 0)
    return room_id, active_view_id


def main(argv: List[str]) -> int:
    ap = argparse.ArgumentParser(
        description=(
            "選択中のRoomについて、各ペリメータ線（外周ループ）の始点・終点座標、長さ、"
            "および近傍の壁タイプを一覧表示します。"
        )
    )
    ap.add_argument("--port", type=int, default=5210, help="Revit MCP ポート番号")
    ap.add_argument("--include-islands", action="store_true", help="島ループも含めて集計する場合に指定")
    args = ap.parse_args(argv)

    port = args.port

    try:
        room_id, _ = ensure_room_selected(port)
    except SystemExit as ex:
        # 既に JSON エラーメッセージを出力済み
        return ex.code if isinstance(ex.code, int) else 1

    gp_params = {
        "roomId": room_id,
        "includeSegments": True,
        "includeIslands": bool(args.include_islands),
        "autoDetectColumnsInRoom": False,
        "includeWallMatches": True,
        "wallMaxOffsetMm": 500.0,
        "wallMinOverlapMm": 50.0,
        "wallMaxAngleDeg": 5.0,
    }

    try:
        gp_env = send_request(port, "get_room_perimeter_with_columns_and_walls", gp_params)
    except RevitMcpError as ex:
        print(
            json.dumps(
                {"ok": False, "code": "RPC_ERROR", "msg": str(ex)},
                ensure_ascii=False,
            )
        )
        return 1

    gp = unwrap(gp_env)
    loops = gp.get("loops") or []
    walls = gp.get("walls") or []

    if not loops:
        print(
            json.dumps(
                {"ok": False, "code": "NO_BOUNDARY", "msg": "Room 境界が取得できませんでした。"},
                ensure_ascii=False,
            )
        )
        return 1

    # loopIndex=0 を外周とみなし、そこを優先
    loops_by_index: Dict[int, Dict[str, Any]] = {}
    for lp in loops:
        idx = int(lp.get("loopIndex"))
        loops_by_index[idx] = lp

    target_loop_indices: List[int]
    if 0 in loops_by_index:
        target_loop_indices = [0]
    else:
        target_loop_indices = sorted(loops_by_index.keys())

    # (loopIndex, segmentIndex) → segment info
    seg_geo: Dict[Tuple[int, int], Dict[str, Any]] = {}
    for idx in target_loop_indices:
        lp = loops_by_index[idx]
        segs = lp.get("segments") or []
        for si, seg in enumerate(segs):
            start = seg.get("start") or {}
            end = seg.get("end") or {}
            sx = float(start.get("x", 0.0))
            sy = float(start.get("y", 0.0))
            sz = float(start.get("z", 0.0))
            ex = float(end.get("x", 0.0))
            ey = float(end.get("y", 0.0))
            ez = float(end.get("z", 0.0))
            length = ((ex - sx) ** 2 + (ey - sy) ** 2) ** 0.5
            seg_geo[(idx, si)] = {
                "loopIndex": idx,
                "segmentIndex": si,
                "start": {"x": sx, "y": sy, "z": sz},
                "end": {"x": ex, "y": ey, "z": ez},
                "lengthMm": length,
            }

    # 各 segment にマッチした壁を集計
    seg_matches: Dict[Tuple[int, int], List[Dict[str, Any]]] = {}
    for w in walls:
        wid = int(w.get("wallId"))
        type_id = int(w.get("typeId") or 0)
        type_name = str(w.get("typeName") or "")
        min_dist = float(w.get("minDistanceMm") or 0.0)
        max_ov = float(w.get("maxOverlapMm") or 0.0)
        for seg_ref in w.get("segments") or []:
            li = int(seg_ref.get("loopIndex"))
            si = int(seg_ref.get("segmentIndex"))
            key = (li, si)
            if key not in seg_geo:
                continue  # 島ループなど、今回の対象外
            seg_matches.setdefault(key, []).append(
                {
                    "wallId": wid,
                    "typeId": type_id,
                    "typeName": type_name,
                    "minDistanceMm": min_dist,
                    "maxOverlapMm": max_ov,
                }
            )

    # 出力用の配列を作成
    rows: List[Dict[str, Any]] = []
    for key in sorted(seg_geo.keys()):
        (li, si) = key
        info = seg_geo[key]
        walls_for_seg = seg_matches.get(key, [])
        rows.append(
            {
                "loopIndex": li,
                "segmentIndex": si,
                "start": info["start"],
                "end": info["end"],
                "lengthMm": info["lengthMm"],
                "walls": walls_for_seg,
            }
        )

    # JSON として標準出力に返す（テキスト整形は呼び出し側に任せる）
    print(
        json.dumps(
            {
                "ok": True,
                "roomId": room_id,
                "loopCount": len(loops_by_index),
                "segmentCount": len(rows),
                "segments": rows,
            },
            ensure_ascii=False,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

