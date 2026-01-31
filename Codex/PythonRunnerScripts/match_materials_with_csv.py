# @feature: match materials with csv | keywords: マテリアル
import argparse
import csv
import json
import os
from difflib import SequenceMatcher
from typing import Any, Dict, List

from send_revit_command_durable import send_request, RevitMcpError


def _unwrap_result(payload: Dict[str, Any]) -> Dict[str, Any]:
    inner = payload.get("result")
    if isinstance(inner, dict) and "result" in inner:
        inner = inner.get("result")
    if isinstance(inner, dict):
        return inner
    return {}


def load_csv_rows(path: str) -> List[Dict[str, Any]]:
    rows: List[Dict[str, Any]] = []
    if not os.path.exists(path):
        raise FileNotFoundError(path)
    # CSV は SJIS 系の可能性が高いので cp932 優先、だめなら UTF-8
    for enc in ("cp932", "utf-8-sig", "utf-8"):
        try:
            with open(path, "r", encoding=enc, errors="ignore") as f:
                reader = csv.reader(f)
                for r in reader:
                    if len(r) < 3:
                        continue
                    name = (r[1] or "").strip()
                    if not name:
                        continue
                    lam = None
                    try:
                        lam = float(str(r[2]).strip())
                    except Exception:
                        lam = None
                    rows.append({"name": name, "lambda": lam})
            break
        except Exception:
            rows = []
            continue
    return rows


def similarity(a: str, b: str) -> float:
    return SequenceMatcher(None, a, b).ratio()


def main() -> None:
    parser = argparse.ArgumentParser(
        description="国交省R3 CSV と Revit プロジェクトのマテリアル名を突き合わせて類似度を出力します。"
    )
    parser.add_argument("--port", type=int, default=5210)
    parser.add_argument(
        "--csv-path",
        type=str,
        default=os.path.join("Work", "熱伝導率_国交省R3.csv"),
        help="国交省R3 熱伝導率 CSV のパス",
    )
    parser.add_argument(
        "--output-file",
        type=str,
        default=os.path.join("Work", "Temp_5210", "material_csv_match.json"),
    )
    args = parser.parse_args()

    os.makedirs(os.path.dirname(args.output_file), exist_ok=True)

    try:
        csv_rows = load_csv_rows(args.csv_path)
    except Exception as e:
        result = {"ok": False, "msg": f"CSV 読み込みエラー: {e}", "csvPath": args.csv_path}
        with open(args.output_file, "w", encoding="utf-8") as f:
            json.dump(result, f, ensure_ascii=False, indent=2)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return

    try:
        mats_env = send_request(
            args.port,
            "get_materials",
            {"_shape": {"page": {"limit": 1000}}},
        )
        mats = _unwrap_result(mats_env)
    except RevitMcpError as e:
        result = {
            "ok": False,
            "msg": f"get_materials 失敗: {e}",
            "where": e.where,
        }
        with open(args.output_file, "w", encoding="utf-8") as f:
            json.dump(result, f, ensure_ascii=False, indent=2)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return

    materials: List[Dict[str, Any]] = mats.get("materials") or []

    matches: List[Dict[str, Any]] = []
    for m in materials:
        m_id = m.get("materialId")
        m_name = (m.get("materialName") or "").strip()
        if not m_name:
            continue

        # 「コンクリート」を含むものを優先対象とする
        is_concrete = "コンクリート" in m_name or "ｺﾝｸﾘｰﾄ" in m_name

        # CSV 側との類似度を計算
        scored: List[Dict[str, Any]] = []
        for row in csv_rows:
            csv_name = row["name"]
            s = similarity(m_name, csv_name)
            if s <= 0.3:
                continue
            scored.append(
                {
                    "csvName": csv_name,
                    "lambda": row["lambda"],
                    "similarity": round(s, 3),
                }
            )
        scored.sort(key=lambda x: x["similarity"], reverse=True)
        top = scored[:5]

        if not top and not is_concrete:
            continue

        matches.append(
            {
                "materialId": m_id,
                "materialName": m_name,
                "materialClass": m.get("materialClass"),
                "isConcreteLike": is_concrete,
                "topMatches": top,
            }
        )

    result = {
        "ok": True,
        "csvPath": os.path.abspath(args.csv_path),
        "totalCsvRows": len(csv_rows),
        "totalMaterials": len(materials),
        "matchedMaterials": matches,
    }

    with open(args.output_file, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)

    print(json.dumps({"ok": True, "savedTo": os.path.abspath(args.output_file)}, ensure_ascii=False))


if __name__ == "__main__":
    main()

