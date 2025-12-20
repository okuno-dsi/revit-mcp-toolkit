import argparse
import json
from pathlib import Path
from typing import List

from tools.mcp_safe import get_element_info_safe, call_mcp


def main() -> None:
    ap = argparse.ArgumentParser(description="Safely fetch element info (chunked + retries)")
    ap.add_argument("--port", type=int, required=True)
    ap.add_argument("--ids", type=str, help="Comma-separated element ids. If omitted, use current selection.")
    ap.add_argument("--output", type=str, default=str(Path("Work") / "element_info_safe.json"))
    ap.add_argument("--batch", type=int, default=8)
    args = ap.parse_args()

    if args.ids:
        ids: List[int] = [int(x) for x in args.ids.split(",") if x.strip()]
    else:
        sel = call_mcp(args.port, "get_selected_element_ids", {})
        top = sel.get("result") or sel
        if isinstance(top, dict) and "result" in top:
            top = top["result"]
        ids = list((top or {}).get("elementIds", []))
    if not ids:
        print(json.dumps({"ok": False, "error": "No element ids provided or selected."}, ensure_ascii=False))
        return

    res = get_element_info_safe(args.port, ids, batch_size=args.batch)
    out = Path(args.output)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(res, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"ok": True, "savedTo": str(out)}, ensure_ascii=False))


if __name__ == "__main__":
    main()

