# @feature: UTF-8(with BOM) 優先 | keywords: 壁, 集計表
import json
import sys
from pathlib import Path
from collections import defaultdict


def load_json(path: Path) -> dict:
    """
    JSON をできるだけ素直に読み込む。
    - UTF-8 (BOM 付き含む)
    - CP932
    の順にトライして、最後にデフォルトエンコーディングにフォールバック。
    PowerShell 経由にすると、Python 側の cp932 デコードでエラーになりやすいので避ける。
    """
    # UTF-8(with BOM) 優先
    encodings = ["utf-8-sig", "utf-8", "cp932", None]
    last_err = None
    for enc in encodings:
        try:
            if enc is None:
                with path.open("r") as f:
                    return json.load(f)
            else:
                with path.open("r", encoding=enc) as f:
                    return json.load(f)
        except Exception as e:  # noqa: PERF203
            last_err = e
            continue
    # ここまで来ることは稀だが、念のためエラーをそのまま出す
    raise last_err


def main() -> None:
    base = Path(".")
    all_path = base / "tmp_all_walls_for_analysis.json"
    ext_path = base / "tmp_exterior_walls_for_analysis.json"

    if not all_path.exists() or not ext_path.exists():
        print("Input JSON files not found. Run get_walls / get_candidate_exterior_walls first.", file=sys.stderr)
        sys.exit(1)

    all_data = load_json(all_path)
    ext_data = load_json(ext_path)

    # RevitMCP の JSON は二重に result でラップされている
    # { jsonrpc, id, result: { jsonrpc, id, method, agentId, result: { ...本体... } } }
    all_res = all_data["result"]["result"]
    ext_res = ext_data["result"]["result"]

    all_walls = all_res.get("walls", [])
    ext_walls = ext_res.get("walls", [])

    ext_ids = {int(w["elementId"]) for w in ext_walls}

    # typeName ごとの集計
    stats = defaultdict(lambda: {"total": 0, "exterior": 0})

    for w in all_walls:
        eid = int(w.get("elementId"))
        tname = w.get("typeName") or ""
        rec = stats[tname]
        rec["total"] += 1
        if eid in ext_ids:
            rec["exterior"] += 1

    # 出力: typeName, total, exterior, interior, exteriorRatio
    rows = []
    for tname, rec in stats.items():
        total = rec["total"]
        exterior = rec["exterior"]
        interior = total - exterior
        ratio = exterior / total if total > 0 else 0.0
        rows.append((tname, total, exterior, interior, ratio))

    # 外周として多く拾われている順にソート
    rows.sort(key=lambda x: (-x[4], -x[1], x[0]))

    print("typeName,total,exterior,interior,exteriorRatio")
    for tname, total, exterior, interior, ratio in rows:
        print(f"{tname},{total},{exterior},{interior},{ratio:.3f}")


if __name__ == "__main__":
    main()
