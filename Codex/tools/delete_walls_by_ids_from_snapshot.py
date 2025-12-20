import argparse
import json
from pathlib import Path
from typing import Any, Dict, List

from tools.mcp_safe import call_mcp


def main() -> None:
    ap = argparse.ArgumentParser(description="Delete walls by elementId listed in a walls snapshot JSON")
    ap.add_argument("--port", type=int, required=True)
    ap.add_argument("--snapshot", type=str, required=True, help="Path to walls_with_coords.json (must include walls[].elementId)")
    ap.add_argument("--out", type=str, default=str(Path("Work")/"大阪ビル"/"Logs"/"delete_walls_by_ids_report.json"))
    args = ap.parse_args()

    snap_path = Path(args.snapshot)
    data = json.loads(snap_path.read_text(encoding="utf-8"))
    walls: List[Dict[str, Any]] = list((data.get("walls") or []))
    ids: List[int] = []
    for w in walls:
        try:
            wid = int(w.get("elementId") or w.get("id") or 0)
            if wid > 0:
                ids.append(wid)
        except Exception:
            pass

    deleted = []
    missing = []
    failed = []
    for wid in ids:
        try:
            # Best-effort delete
            res = call_mcp(args.port, "delete_wall", {"elementId": wid}, retries=2, base_wait=0.5, max_wait_seconds=30.0)
            # If server returns ok false, treat as missing/failed
            top = res.get("result") or res
            if isinstance(top, dict) and top.get("ok") is False:
                # Consider as missing if message contains not found
                msg = (top.get("error") or top.get("msg") or "").lower()
                if "not found" in msg:
                    missing.append(wid)
                else:
                    failed.append({"id": wid, "res": top})
            else:
                deleted.append(wid)
        except Exception as ex:
            failed.append({"id": wid, "error": str(ex)})

    report = {
        "ok": len(failed) == 0,
        "requested": len(ids),
        "deleted": len(deleted),
        "missing": len(missing),
        "failed": failed,
    }
    Path(args.out).parent.mkdir(parents=True, exist_ok=True)
    Path(args.out).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False))


if __name__ == "__main__":
    main()

