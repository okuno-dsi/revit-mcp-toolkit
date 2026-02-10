import argparse
import json
import os
import sys
from typing import Any, Dict, List, Tuple


def _add_scripts_to_path() -> None:
    here = os.path.dirname(os.path.abspath(__file__))
    if here not in sys.path:
        sys.path.insert(0, here)


_add_scripts_to_path()

try:
    # Prefer UTF-8 stdout (best effort; ignore failure)
    sys.stdout.reconfigure(encoding="utf-8")  # type: ignore[attr-defined]
except Exception:
    pass

from send_revit_command_durable import send_request, RevitMcpError  # type: ignore  # noqa: E402


def unwrap(payload: Dict[str, Any]) -> Dict[str, Any]:
    """Unwrap JSON-RPC envelope down to the innermost { ... } dict."""
    obj: Any = payload
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict):
        return obj
    return {}


def extract_room_params(params: List[Dict[str, Any]]) -> Tuple[str, str]:
    """
    Extract commonly used parameters from get_room_params payload.

    Returns:
        (roomNumber, liveLoad)
    """
    number_val: Any = None
    live_load_val: Any = None

    for p in params:
        name = (p.get("name") or "").strip()
        if name == "番号" and number_val is None:
            number_val = p.get("display") or p.get("value") or p.get("raw")
        elif name == "積載荷重" and live_load_val is None:
            live_load_val = p.get("display") or p.get("value") or p.get("raw")

    def _to_str(x: Any) -> str:
        return str(x).strip() if x is not None else ""

    return _to_str(number_val), _to_str(live_load_val)


def main(argv: List[str]) -> int:
    ap = argparse.ArgumentParser(
        description=(
            "Export Rooms with selected parameters using get_rooms + get_room_params.\n"
            "The result JSON is suitable for feeding into LLMs (project-wide room summary)."
        )
    )
    ap.add_argument("--port", type=int, default=5210, help="Revit MCP port")
    ap.add_argument(
        "--max-rooms",
        type=int,
        default=0,
        help="Maximum number of Rooms to process (0 = no limit)",
    )
    ap.add_argument(
        "--output-file",
        type=str,
        default="",
        help="Output JSON path. If omitted, JSON is printed to stdout.",
    )
    args = ap.parse_args(argv)

    port = int(args.port)

    try:
        # 1) Collect all Rooms (basic info)
        rooms_env = send_request(port, "get_rooms", {"skip": 0, "count": 0})
        rooms_res = unwrap(rooms_env)
        rooms: List[Dict[str, Any]] = rooms_res.get("rooms") or []

        if args.max_rooms and args.max_rooms > 0:
            rooms = rooms[: int(args.max_rooms)]

        combined: List[Dict[str, Any]] = []

        for r in rooms:
            try:
                rid = int(r.get("elementId"))
            except Exception:
                continue
            if rid <= 0:
                continue

            # 2) Fetch parameters for this Room
            rp_env = send_request(port, "get_room_params", {"roomId": rid, "skip": 0, "count": 300})
            rp = unwrap(rp_env)
            params: List[Dict[str, Any]] = rp.get("parameters") or []

            number_val, live_load_val = extract_room_params(params)

            room_obj: Dict[str, Any] = {
                "elementId": rid,
                "uniqueId": r.get("uniqueId") or "",
                "name": r.get("name") or "",
                "number": number_val or r.get("number") or "",
                "level": r.get("level") or "",
                "area": r.get("area"),
                "state": r.get("state") or "",
                "center": r.get("center") or {},
                "parameters": params,
            }
            if live_load_val:
                room_obj["liveLoad"] = live_load_val

            combined.append(room_obj)

        result: Dict[str, Any] = {
            "ok": True,
            "totalCount": len(combined),
            "rooms": combined,
        }

    except RevitMcpError as e:
        result = {
            "ok": False,
            "where": e.where,
            "httpStatus": e.http_status,
            "error": str(e),
            "payload": e.payload,
        }
    except Exception as e:
        result = {
            "ok": False,
            "error": str(e),
        }

    out_json = json.dumps(result, ensure_ascii=False, indent=2)

    if args.output_file:
        out_path = os.path.abspath(args.output_file)
        out_dir = os.path.dirname(out_path)
        if out_dir and not os.path.exists(out_dir):
            os.makedirs(out_dir, exist_ok=True)
        with open(out_path, "w", encoding="utf-8") as f:
            f.write(out_json)

        # Match the style of send_revit_command_durable.py when using --output-file
        print(json.dumps({"ok": bool(result.get("ok", False)), "savedTo": out_path}, ensure_ascii=False))
    else:
        print(out_json)

    return 0 if result.get("ok") else 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

