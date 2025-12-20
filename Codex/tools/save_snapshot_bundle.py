import argparse
import json
from pathlib import Path
from typing import Any, Dict

from tools.mcp_safe import call_mcp


def unwrap(res: Dict[str, Any]) -> Dict[str, Any]:
    top = res.get("result") or res
    if isinstance(top, dict) and "result" in top:
        top = top["result"]
    return top if isinstance(top, dict) else {}


def main() -> None:
    ap = argparse.ArgumentParser(description="Save a reconstruction snapshot bundle (levels, grids, walls, doors, windows, rooms)")
    ap.add_argument("--port", type=int, required=True)
    ap.add_argument("--out", type=str, default=str(Path("Work")/"snapshot_bundle.json"))
    args = ap.parse_args()

    port = args.port
    bundle: Dict[str, Any] = {"ok": True}

    # Basic: levels/grids
    bundle["levels"] = unwrap(call_mcp(port, "get_levels", {"skip": 0, "count": 500})).get("levels", [])
    try:
        bundle["grids"] = unwrap(call_mcp(port, "get_grids", {"skip": 0, "count": 1000})).get("grids", [])
    except Exception:
        bundle["grids"] = []

    # Elements: walls/doors/windows/rooms
    bundle["walls"] = unwrap(call_mcp(port, "get_walls", {"skip": 0, "count": 5000})).get("walls", [])
    try:
        bundle["rooms"] = unwrap(call_mcp(port, "get_rooms", {"skip": 0, "count": 5000})).get("rooms", [])
    except Exception:
        bundle["rooms"] = []
    try:
        bundle["doors"] = unwrap(call_mcp(port, "get_doors", {"skip": 0, "count": 5000})).get("doors", [])
    except Exception:
        bundle["doors"] = []
    try:
        bundle["windows"] = unwrap(call_mcp(port, "get_windows", {"skip": 0, "count": 5000})).get("windows", [])
    except Exception:
        bundle["windows"] = []

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(bundle, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"ok": True, "savedTo": str(out)}, ensure_ascii=False))


if __name__ == "__main__":
    main()

