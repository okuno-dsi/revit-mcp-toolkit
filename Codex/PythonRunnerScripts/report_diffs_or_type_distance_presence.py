#!/usr/bin/env python
# -*- coding: utf-8 -*-
# @feature: report diffs or type distance presence | keywords: ビュー

import json
import sys
import csv
from pathlib import Path
from typing import Any, Dict, List, Tuple, Optional


def load(path: str) -> Dict[str, Any]:
    with open(path, 'r', encoding='utf-8') as f:
        return json.load(f)


def centroid_mm(e: Dict[str, Any]) -> Tuple[float, float, float]:
    bb = e.get('bboxMm')
    if isinstance(bb, dict) and 'min' in bb and 'max' in bb:
        try:
            mn = bb['min']; mx = bb['max']
            return (
                0.5 * (float(mn.get('x', 0.0)) + float(mx.get('x', 0.0))),
                0.5 * (float(mn.get('y', 0.0)) + float(mx.get('y', 0.0))),
                0.5 * (float(mn.get('z', 0.0)) + float(mx.get('z', 0.0))),
            )
        except Exception:
            pass
    cm = e.get('coordinatesMm') or {}
    try:
        return (float(cm.get('x', 0.0)), float(cm.get('y', 0.0)), float(cm.get('z', 0.0)))
    except Exception:
        return (0.0, 0.0, 0.0)


def distance_mm(a: Tuple[float, float, float], b: Tuple[float, float, float]) -> float:
    dx = a[0] - b[0]
    dy = a[1] - b[1]
    dz = a[2] - b[2]
    return (dx*dx + dy*dy + dz*dz) ** 0.5


def is_structural(e: Dict[str, Any]) -> bool:
    cid = e.get('categoryId')
    try:
        c = int(cid)
        return c in (-2001320, -2001330)
    except Exception:
        return False


def approx_length_mm(e: Dict[str, Any]) -> float:
    bb = e.get('bboxMm')
    if isinstance(bb, dict) and 'min' in bb and 'max' in bb:
        try:
            mn = bb['min']; mx = bb['max']
            dx = float(mx.get('x', 0.0)) - float(mn.get('x', 0.0))
            dy = float(mx.get('y', 0.0)) - float(mn.get('y', 0.0))
            dz = float(mx.get('z', 0.0)) - float(mn.get('z', 0.0))
            return max(abs(dx), abs(dy), abs(dz))
        except Exception:
            return 0.0
    return 0.0

def pick_pairs_and_unmatched(left: List[Dict[str, Any]], right: List[Dict[str, Any]], pos_tol: float = 600.0, len_tol: float = 150.0):
    # Greedy nearest-neighbor with distance/length gating (in mm)
    right_used = [False] * len(right)
    pairs: List[Tuple[Dict[str, Any], Dict[str, Any], float]] = []
    L = [(i, centroid_mm(e), approx_length_mm(e)) for i, e in enumerate(left)]
    R = [(i, centroid_mm(e), approx_length_mm(e)) for i, e in enumerate(right)]
    for li, lc, ll in L:
        best = None
        bestj = -1
        for rj, rc, rl in R:
            if right_used[rj]:
                continue
            d = distance_mm(lc, rc)
            if d > pos_tol:
                continue
            if ll > 0 and rl > 0 and abs(ll - rl) > len_tol:
                continue
            # prefer same familyName if tie
            fam_l = str(left[li].get('familyName') or '')
            fam_r = str(right[rj].get('familyName') or '')
            score = d + (0.0 if fam_l == fam_r else 1.0)  # small bias
            if best is None or score < best:
                best = score
                bestj = rj
        if bestj >= 0:
            right_used[bestj] = True
            d = distance_mm(L[li][1], R[bestj][1])
            pairs.append((left[li], right[bestj], d))
    unmatched_left = [left[i] for i, _lc, _ll in L if all(p[0] is not left[i] for p in pairs)]
    unmatched_right = [right[j] for j, u in enumerate(right_used) if not u]
    return pairs, unmatched_left, unmatched_right


def get_param_value(typedict: Dict[str, Any], type_id: Optional[int], key: str) -> Optional[str]:
    if type_id is None:
        return None
    if not isinstance(typedict, dict):
        return None
    item = typedict.get(str(type_id)) or typedict.get(type_id)
    if not isinstance(item, dict):
        return None
    params = item.get('params') or {}
    display = item.get('display') or {}
    if key in params:
        v = params.get(key)
        return str(v) if v is not None else None
    if key in display:
        v = display.get(key)
        return str(v) if v is not None else None
    return None

def main():
    if len(sys.argv) < 4:
        print("Usage: report_diffs_or_type_distance_presence.py <left.json> <right.json> <out.csv>")
        sys.exit(2)
    left_path, right_path, out_csv = sys.argv[1], sys.argv[2], sys.argv[3]
    left = load(left_path)
    right = load(right_path)
    lelems = [e for e in (left.get('elements') or []) if is_structural(e)]
    relems = [e for e in (right.get('elements') or []) if is_structural(e)]
    ltypes = left.get('typeParameters') or {}
    rtypes = right.get('typeParameters') or {}

    # Strict pairing with distance/length gating to avoid mispairing
    # Tolerances tuned for framing elements in plan views
    pairs, l_only_tmp, r_only = pick_pairs_and_unmatched(lelems, relems, pos_tol=600.0, len_tol=150.0)
    # For left_unmatched: if left has more than right, extra left elements are unmatched
    # Greedy above takes first N from left to pair; treat the rest as unmatched
    # (already computed in l_only_tmp)

    rows: List[List[Any]] = []
    # Pairs: OR of (typeName differs or type-params differ) OR (distanceMm >= 30)
    keys = ['符号','H','B','tw','tf']
    for el, er, dist in pairs:
        tname_diff = (str(el.get('typeName') or '') != str(er.get('typeName') or ''))
        # type parameter diffs
        tidL = el.get('typeId'); tidR = er.get('typeId')
        tparam_diff = False
        for k in keys:
            vL = get_param_value(ltypes, tidL, k)
            vR = get_param_value(rtypes, tidR, k)
            if vL is not None and vR is not None and str(vL) != str(vR):
                tparam_diff = True
                break
        type_diff = tname_diff or tparam_diff
        pos_diff = (dist >= 30.0)
        if type_diff or pos_diff:
            # Report both sides
            portL = int(left.get('port') or 5210)
            portR = int(right.get('port') or 5211)
            for side, port, e in (("left", portL, el), ("right", portR, er)):
                rows.append([
                    side,
                    port,
                    int(e.get('elementId') or 0),
                    int(e.get('categoryId') or 0),
                    str(e.get('familyName') or ''),
                    str(e.get('typeName') or ''),
                    round(dist, 1),
                    ("type_name" if tname_diff else ("type_param" if tparam_diff else "")),
                    "pos_ge_30mm" if pos_diff else ""
                ])

    # Unmatched (presence difference): report as OR condition too
    portL = int(left.get('port') or 5210)
    portR = int(right.get('port') or 5211)
    for e in l_only_tmp:
        rows.append(["left", portL, int(e.get('elementId') or 0), int(e.get('categoryId') or 0), str(e.get('familyName') or ''), str(e.get('typeName') or ''), "", "", "only_in_left"])
    for e in r_only:
        rows.append(["right", portR, int(e.get('elementId') or 0), int(e.get('categoryId') or 0), str(e.get('familyName') or ''), str(e.get('typeName') or ''), "", "", "only_in_right"])

    Path(out_csv).parent.mkdir(parents=True, exist_ok=True)
    with open(out_csv, 'w', newline='', encoding='utf-8') as f:
        w = csv.writer(f)
        w.writerow(["side","port","elementId","categoryId","familyName","typeName","distanceMm","typeDiff","presenceOrPos"])
        w.writerows(rows)

    print(json.dumps({"ok": True, "count": len(rows), "csv": str(out_csv)}, ensure_ascii=False))


if __name__ == '__main__':
    main()
