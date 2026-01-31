# @feature: apply room perimeter debug view | keywords: 柱, 壁, 部屋, ビュー
import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, List


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


def duplicate_view_with_suffix(port: int, view_id: int, suffix: str) -> int:
    """アクティブビューを複製し、名前に suffix を付けて返す。"""
    info = unwrap(send_request(port, "get_view_info", {"viewId": view_id}))
    view = info.get("view") or info
    base_name = str(view.get("name") or f"View_{view_id}")

    dup = unwrap(send_request(port, "duplicate_view", {"viewId": view_id, "__smoke_ok": True}))
    new_vid = int(dup.get("viewId") or dup.get("newViewId") or 0)
    if new_vid <= 0:
        raise RuntimeError("duplicate_view から viewId が取得できませんでした。")

    new_name = f"{base_name} {suffix}".strip()
    try:
        send_request(port, "rename_view", {"viewId": new_vid, "newName": new_name})
    except RevitMcpError:
        # 名前重複などは無視して進む
        pass

    # ビューをアクティブにしてテンプレート解除
    try:
        send_request(port, "activate_view", {"viewId": new_vid})
    except RevitMcpError:
        pass

    try:
        send_request(port, "set_view_template", {"viewId": new_vid, "clear": True})
    except RevitMcpError:
        # テンプレート未設定などは無視
        pass

    return new_vid


def apply_visual_override(port: int, view_id: int, element_ids: List[int], r: int, g: int, b: int, transparency: int) -> None:
    if not element_ids:
        return
    params = {
        "viewId": view_id,
        "elementIds": element_ids,
        "autoWorkingView": False,
        "detachViewTemplate": False,
        "r": int(r),
        "g": int(g),
        "b": int(b),
        "transparency": int(transparency),
        "refreshView": True,
    }
    send_request(port, "set_visual_override", params)


def main(argv: List[str]) -> int:
    ap = argparse.ArgumentParser(
        description="選択中のRoomについて、外周線を詳細線で描画し、柱/壁/外周線を着色するデバッグ用スクリプト。"
    )
    ap.add_argument("--port", type=int, default=5210, help="Revit MCP ポート番号")
    ap.add_argument("--suffix", type=str, default="Finish_RoomPerimeter", help="複製ビュー名のサフィックス")
    args = ap.parse_args(argv)

    port = args.port

    # 1) 選択中の Room とアクティブビュー
    sel_env = send_request(port, "get_selected_element_ids", {})
    sel = unwrap(sel_env)
    ids = sel.get("elementIds") or []
    if not ids:
        print(json.dumps({"ok": False, "code": "NO_SELECTION", "msg": "Room を1つ選択してから実行してください。"}, ensure_ascii=False))
        return 1

    room_id = int(ids[0])
    active_view_id = int(sel.get("activeViewId") or 0)
    if active_view_id <= 0:
        # 念のため get_current_view で補う
        cur = unwrap(send_request(port, "get_current_view", {}))
        active_view_id = int(cur.get("viewId") or 0)

    if active_view_id <= 0:
        print(json.dumps({"ok": False, "code": "NO_VIEW", "msg": "アクティブビューIDが取得できませんでした。"}, ensure_ascii=False))
        return 1

    # 2) ビュー複製 + テンプレート解除
    new_view_id = duplicate_view_with_suffix(port, active_view_id, args.suffix)

    # 3) Room の外周 + 壁・柱情報を取得
    gp_params = {
        "roomId": room_id,
        "includeSegments": True,
        "includeIslands": True,
        "autoDetectColumnsInRoom": True,
        "searchMarginMm": 1000.0,
        "includeWallMatches": True,
        "wallMaxOffsetMm": 500.0,
        "wallMinOverlapMm": 50.0,
        "wallMaxAngleDeg": 5.0,
    }
    gp_env = send_request(port, "get_room_perimeter_with_columns_and_walls", gp_params)
    gp = unwrap(gp_env)
    loops = gp.get("loops") or []
    if not loops:
        print(json.dumps({"ok": False, "code": "NO_BOUNDARY", "msg": "Room の境界線が取得できませんでした。"}, ensure_ascii=False))
        return 1

    # 外周ループ: loopIndex=0 を優先（なければ最長周長のループ）
    outer = None
    for lp in loops:
        if lp.get("loopIndex") == 0:
            outer = lp
            break
    if outer is None:
        # fallback: 最長周長
        best = None
        best_perim = -1.0
        for lp in loops:
            segs = lp.get("segments") or []
            perim = 0.0
            for seg in segs:
                s = seg.get("start") or {}
                e = seg.get("end") or {}
                dx = float(e.get("x", 0.0)) - float(s.get("x", 0.0))
                dy = float(e.get("y", 0.0)) - float(s.get("y", 0.0))
                perim += (dx * dx + dy * dy) ** 0.5
            if perim > best_perim:
                best_perim = perim
                best = lp
        outer = best

    if outer is None:
        print(json.dumps({"ok": False, "code": "NO_OUTER_LOOP", "msg": "外周ループが特定できませんでした。"}, ensure_ascii=False))
        return 1

    segments = outer.get("segments") or []

    basis = gp.get("basis") or {}
    col_ids = [int(i) for i in (basis.get("autoDetectedColumnIds") or [])]

    wall_ids = [int(w.get("wallId")) for w in (gp.get("walls") or []) if w.get("wallId") is not None]

    # 4) 外周を詳細線で描画（太線）
    detail_ids: List[int] = []
    for seg in segments:
        s = seg.get("start") or {}
        e = seg.get("end") or {}
        params = {
            "viewId": new_view_id,
            "start": {"x": float(s.get("x", 0.0)), "y": float(s.get("y", 0.0)), "z": float(s.get("z", 0.0))},
            "end": {"x": float(e.get("x", 0.0)), "y": float(e.get("y", 0.0)), "z": float(e.get("z", 0.0))},
            "styleName": "<太線>",
        }
        try:
            resp = unwrap(send_request(port, "create_detail_line", params))
            eid = int(resp.get("elementId") or 0)
            if eid > 0:
                detail_ids.append(eid)
        except RevitMcpError as ex:
            # 1本失敗しても他は続行
            sys.stderr.write(f"create_detail_line failed: {ex}\n")

    # 5) 色設定: 柱=黄色, ペリメータ=赤, 壁=薄青
    apply_visual_override(port, new_view_id, col_ids, 255, 255, 0, 50)
    apply_visual_override(port, new_view_id, wall_ids, 128, 192, 255, 40)
    apply_visual_override(port, new_view_id, detail_ids, 255, 0, 0, 0)

    print(json.dumps(
        {
            "ok": True,
            "roomId": room_id,
            "viewId": new_view_id,
            "columnsColored": len(col_ids),
            "wallsColored": len(wall_ids),
            "detailLinesCreated": len(detail_ids),
        },
        ensure_ascii=False,
    ))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

