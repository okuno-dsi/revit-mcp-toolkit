# @feature: export exterior wall baselines | keywords: 壁, 部屋, エリア, ビュー
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
    obj: Any = payload
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict):
        return obj
    return {}


def get_active_view_id(port: int) -> int:
    env = send_request(port, "get_selected_element_ids", {"fallbackToStash": True, "maxAgeMs": 600000})
    sel = unwrap(env)
    return int(sel.get("activeViewId") or 0)


def get_candidate_exterior_wall_ids(port: int, view_id: int) -> List[int]:
    env = send_request(
        port,
        "get_candidate_exterior_walls",
        {
            "viewId": view_id,
            "roomCheck": True,
            "offsetMm": 1000.0,
            "minExteriorAreaM2": 0.1,
        },
    )
    res = unwrap(env)
    walls = res.get("walls") or []
    ids: List[int] = []
    for w in walls:
        try:
            ids.append(int(w.get("elementId") or w.get("wallId") or 0))
        except Exception:
            continue
    # fallback: some implementations might expose "candidates" instead of "walls"
    if not ids and res.get("candidates"):
        for w in res.get("candidates") or []:
            try:
                ids.append(int(w.get("elementId") or w.get("wallId") or 0))
            except Exception:
                continue
    return sorted({i for i in ids if i > 0})


def get_wall_baseline(port: int, wall_id: int) -> Dict[str, Any]:
    env = send_request(port, "get_wall_baseline", {"elementId": wall_id})
    return unwrap(env)


def main(argv: List[str]) -> int:
    ap = argparse.ArgumentParser(
        description=(
            "get_candidate_exterior_walls と get_wall_baseline を組み合わせて、"
            "外壁候補の基準線（LocationCurve）を JSON で書き出します。"
        )
    )
    ap.add_argument("--port", type=int, default=5210, help="Revit MCP ポート番号")
    ap.add_argument(
        "--output-json",
        type=str,
        default="Work/tmp_exterior_wall_baselines.json",
        help="書き出し先 JSON パス（Codex ルート基準）",
    )
    args = ap.parse_args(argv)

    try:
        view_id = get_active_view_id(args.port)
    except RevitMcpError as ex:
        print(json.dumps({"ok": False, "code": "MCP_ERROR", "msg": str(ex)}, ensure_ascii=False, indent=2))
        return 1

    if view_id <= 0:
        print(json.dumps({"ok": False, "code": "NO_VIEW", "msg": "アクティブビューが取得できませんでした。"}, ensure_ascii=False))
        return 1

    try:
        wall_ids = get_candidate_exterior_wall_ids(args.port, view_id)
    except RevitMcpError as ex:
        print(json.dumps({"ok": False, "code": "MCP_ERROR", "msg": str(ex)}, ensure_ascii=False, indent=2))
        return 1

    baselines: List[Dict[str, Any]] = []
    errors: List[Dict[str, Any]] = []

    for wid in wall_ids:
        try:
            base = get_wall_baseline(args.port, wid)
        except RevitMcpError as ex:
            errors.append({"wallId": wid, "reason": str(ex)})
            continue
        if not base.get("ok"):
            errors.append({"wallId": wid, "reason": base.get("msg", "")})
            continue
        baselines.append(
            {
                "wallId": wid,
                "baseline": base.get("baseline"),
            }
        )

    out_obj = {
        "ok": True,
        "viewId": view_id,
        "wallCount": len(wall_ids),
        "baselineCount": len(baselines),
        "baselines": baselines,
        "errors": errors,
    }

    print(json.dumps(out_obj, ensure_ascii=False, indent=2))

    try:
        codex_root = Path(__file__).resolve().parents[2]
        out_path = codex_root / args.output_json
        out_path.parent.mkdir(parents=True, exist_ok=True)
        out_path.write_text(json.dumps(out_obj, ensure_ascii=False, indent=2), encoding="utf-8")
    except Exception:
        pass

    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

