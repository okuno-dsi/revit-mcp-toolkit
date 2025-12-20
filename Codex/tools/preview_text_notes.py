import argparse
import json
import math
import os
from typing import Any, Dict, List

# Local import from repo root
import importlib.util as _iu
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SEND_PATH_CANDIDATES = [
    ROOT / "send_revit_command.py",
    # Default tool location for local LLM + MCP utilities
    ROOT.parents[1] / "NVIDIA-Nemotron-v3" / "tool" / "send_revit_command.py",
]
SEND_PATH = next((p for p in SEND_PATH_CANDIDATES if p.exists()), None)
if SEND_PATH is None:
    raise SystemExit(f"send_revit_command.py not found. Tried: {[str(p) for p in SEND_PATH_CANDIDATES]}")
spec = _iu.spec_from_file_location("send_revit_command", SEND_PATH)
if spec is None or spec.loader is None:
    raise SystemExit(f"Failed to load send_revit_command.py from: {SEND_PATH}")
send_revit_command = _iu.module_from_spec(spec)
spec.loader.exec_module(send_revit_command)


def is_finite_number(x: Any) -> bool:
    try:
        f = float(x)
        return math.isfinite(f)
    except Exception:
        return False


def validate_xyz(x: Any, y: Any, z: Any) -> (bool, str):
    if not (is_finite_number(x) and is_finite_number(y) and is_finite_number(z)):
        return False, "non-finite xyz"
    # z in plan views is typically the view plane. Accept any finite; warn if 0
    if float(x) == 0.0 and float(y) == 0.0:
        return False, "x,y both zero"
    return True, "ok"


def main() -> None:
    ap = argparse.ArgumentParser(description="Preview and optionally send create_text_note at room label points")
    ap.add_argument("--port", type=int, required=True)
    ap.add_argument("--send", action="store_true", help="Actually send create_text_note for valid rows")
    ap.add_argument("--prefix", type=str, default="", help="Optional prefix to add before room name in note")
    ap.add_argument("--include-area", action="store_true", help="Append area to text e.g. ' (98 m2)'")
    ap.add_argument("--output", type=str, default=str(ROOT / "Work" / "text_note_preview.json"))
    args = ap.parse_args()

    out_path = Path(args.output).resolve()
    out_path.parent.mkdir(parents=True, exist_ok=True)

    # 1) Get rooms
    rooms_res = send_revit_command.send_revit_request(args.port, "get_rooms", {"skip": 0, "count": 2000})
    # unwrap potential nested result structures
    result = rooms_res.get("result") or rooms_res
    if isinstance(result, dict) and "result" in result:
        result = result["result"]
    rooms: List[Dict[str, Any]] = list((result or {}).get("rooms", []))
    rooms = [r for r in rooms if r.get("state") == "Placed"]

    rows: List[Dict[str, Any]] = []
    for r in rooms:
        rid = int(r.get("elementId"))
        name = r.get("name")
        area = r.get("area")
        # 2) label point per room
        lp_res = send_revit_command.send_revit_request(args.port, "get_room_label_point", {"elementId": rid})
        lp_top = lp_res.get("result") or lp_res
        if isinstance(lp_top, dict) and "result" in lp_top:
            lp_top = lp_top["result"]
        lp = (lp_top or {}).get("labelPoint", {})
        x = lp.get("x", 0.0)
        y = lp.get("y", 0.0)
        z = lp.get("z", 0.0)

        text = f"{args.prefix}{name}"
        if args.include_area and area is not None:
            try:
                text += f" ({float(area):g} m2)"
            except Exception:
                pass

        valid, reason = validate_xyz(x, y, z)
        row = {
            "elementId": rid,
            "name": name,
            "area": area,
            "labelPoint": {"x": x, "y": y, "z": z},
            "text": text,
            "valid": valid,
            "why": reason,
        }
        rows.append(row)

    # Save preview
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump({"ok": True, "count": len(rows), "items": rows}, f, ensure_ascii=False, indent=2)

    # Print concise table
    print(f"Saved preview -> {out_path}")
    for r in rows:
        lp = r["labelPoint"]
        mark = "OK" if r["valid"] else f"NG:{r['why']}"
        print(f"- id={r['elementId']} name={r['name']} text='{r['text']}' xyz=({lp['x']},{lp['y']},{lp['z']}) {mark}")

    if not args.send:
        return

    # Send only valid ones using top-level x/y/z arguments
    sent = 0
    for r in rows:
        if not r["valid"]:
            continue
        lp = r["labelPoint"]
        payload = {"text": r["text"], "x": lp["x"], "y": lp["y"], "z": lp["z"]}
        _ = send_revit_command.send_revit_request(args.port, "create_text_note", payload)
        sent += 1
    print(f"Sent text notes: {sent}")


if __name__ == "__main__":
    main()
