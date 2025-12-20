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
    # Prefer UTF-8 for stdout (best-effort)
    sys.stdout.reconfigure(encoding="utf-8")  # type: ignore[attr-defined]
except Exception:
    pass

from send_revit_command_durable import send_request, RevitMcpError  # type: ignore  # noqa: E402


LIVE_LOAD_PARAM = "\u7a4d\u8f09\u8377\u91cd"  # 積載荷重
COMMENT_PARAM = "\u30b3\u30e1\u30f3\u30c8"  # コメント


def unwrap(payload: Dict[str, Any]) -> Dict[str, Any]:
    obj: Any = payload
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict):
        return obj
    return {}


def find_latest_mapping_json(port: int) -> str:
    """
    Find the latest create_room_masses_*.json mapping file
    under Work/Project_<port>/Logs.
    """
    # Codex repo root (this script lives in Manuals/Scripts)
    repo_root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    work_dir = os.path.join(repo_root, "Work", f"Project_{port}", "Logs")
    if not os.path.isdir(work_dir):
        raise FileNotFoundError(f"Work log directory not found: {work_dir}")

    candidates: List[Tuple[float, str]] = []
    for name in os.listdir(work_dir):
        if not name.startswith("create_room_masses_") or not name.endswith(".json"):
            continue
        full = os.path.join(work_dir, name)
        try:
            stat = os.stat(full)
            candidates.append((stat.st_mtime, full))
        except OSError:
            continue

    if not candidates:
        raise FileNotFoundError(f"No create_room_masses_*.json files found in {work_dir}")

    candidates.sort(key=lambda x: x[0], reverse=True)
    return candidates[0][1]


def load_room_mass_mapping(path: str) -> List[Dict[str, int]]:
    with open(path, "r", encoding="utf-8") as f:
        data = json.load(f)

    obj = data
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if not isinstance(obj, dict):
        raise ValueError("Unexpected JSON shape in mapping file.")

    created = obj.get("created") or []
    rows: List[Dict[str, int]] = []
    for row in created:
        try:
            rid = int(row.get("roomId"))
            mid = int(row.get("massId"))
        except Exception:
            continue
        if rid > 0 and mid > 0:
            rows.append({"roomId": rid, "massId": mid})
    if not rows:
        raise ValueError("No valid roomId/massId pairs found in mapping file.")
    return rows


def get_room_live_load(port: int, room_id: int) -> str:
    rp_outer = send_request(port, "get_room_params", {"roomId": int(room_id)})
    rp = unwrap(rp_outer)
    params = rp.get("parameters") or []

    load_val: Any = None
    for p in params:
        n = (p.get("name") or "").strip()
        if n == LIVE_LOAD_PARAM and load_val is None:
            load_val = p.get("display") or p.get("value") or p.get("raw")

    if load_val is None:
        return "-"
    s = str(load_val).strip()
    return s if s else "-"


def update_mass_comment(port: int, mass_id: int, live_load: str) -> Dict[str, Any]:
    comment = f"積載荷重：{live_load}"
    payload = {
        "elementId": int(mass_id),
        "paramName": COMMENT_PARAM,
        "value": comment,
    }
    res = send_request(port, "update_direct_shape_parameter", payload)
    return unwrap(res)


def main(argv: List[str]) -> int:
    ap = argparse.ArgumentParser(
        description=(
            "Update DirectShape Room masses' コメント parameter to '積載荷重：<value>' "
            "using create_room_masses_* mapping logs and get_room_params."
        )
    )
    ap.add_argument("--port", type=int, default=5210, help="Revit MCP port")
    ap.add_argument(
        "--mapping-json",
        type=str,
        default="",
        help=(
            "Path to create_room_masses_*.json. "
            "If omitted, the latest file under Work/Project_<port>/Logs is used."
        ),
    )
    args = ap.parse_args(argv)

    port = int(args.port)

    try:
        mapping_path = args.mapping_json or find_latest_mapping_json(port)
        rows = load_room_mass_mapping(mapping_path)

        updated = 0
        errors: List[Dict[str, Any]] = []

        for row in rows:
            room_id = int(row["roomId"])
            mass_id = int(row["massId"])
            if room_id <= 0 or mass_id <= 0:
                continue

            try:
                live_load = get_room_live_load(port, room_id)
                res = update_mass_comment(port, mass_id, live_load)
                if not res.get("ok", True):
                    errors.append(
                        {
                            "roomId": room_id,
                            "massId": mass_id,
                            "liveLoad": live_load,
                            "error": res,
                        }
                    )
                else:
                    updated += 1
            except RevitMcpError as e:
                errors.append(
                    {
                        "roomId": room_id,
                        "massId": mass_id,
                        "liveLoad": None,
                        "error": {
                            "where": e.where,
                            "message": str(e),
                            "payload": e.payload,
                        },
                    }
                )
            except Exception as e:
                errors.append(
                    {
                        "roomId": room_id,
                        "massId": mass_id,
                        "liveLoad": None,
                        "error": {"message": repr(e)},
                    }
                )

        summary = {
            "ok": len(errors) == 0,
            "updatedCount": updated,
            "totalPairs": len(rows),
            "mappingPath": mapping_path,
            "errors": errors,
        }
        print(json.dumps(summary, ensure_ascii=False, indent=2))
        return 0 if summary["ok"] else 1

    except Exception as e:
        err = {
            "ok": False,
            "code": "UNEXPECTED_ERROR",
            "message": repr(e),
        }
        print(json.dumps(err, ensure_ascii=False, indent=2))
        return 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
