#!/usr/bin/env python
# -*- coding: utf-8 -*-
# @feature: strict crossport diff | keywords: レベル

import json
import sys
from pathlib import Path
from typing import Any, Dict, List, Tuple, Optional


def load(path: str) -> Dict[str, Any]:
    with open(path, 'r', encoding='utf-8') as f:
        return json.load(f)


def centroid_mm(e: Dict[str, Any]) -> Tuple[float, float, float]:
    bb = e.get('bboxMm') or (e.get('bbox') if isinstance(e.get('bbox'), dict) else None)
    if isinstance(bb, dict) and 'min' in bb and 'max' in bb:
        try:
            mn = bb['min']; mx = bb['max']
            cx = 0.5 * (float(mn.get('x', 0.0)) + float(mx.get('x', 0.0)))
            cy = 0.5 * (float(mn.get('y', 0.0)) + float(mx.get('y', 0.0)))
            cz = 0.5 * (float(mn.get('z', 0.0)) + float(mx.get('z', 0.0)))
            return (cx, cy, cz)
        except Exception:
            pass
    cm = e.get('coordinatesMm') or {}
    try:
        return (float(cm.get('x', 0.0)), float(cm.get('y', 0.0)), float(cm.get('z', 0.0)))
    except Exception:
        return (0.0, 0.0, 0.0)


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


def elem_group_key(e: Dict[str, Any]) -> Tuple[int, str, str]:
    cat = e.get('categoryId')
    if cat is None and isinstance(e.get('category'), dict):
        cat = e['category'].get('id')
    try:
        cat = int(cat) if cat is not None else 0
    except Exception:
        cat = 0
    fam = e.get('familyName') or (e.get('type') or {}).get('familyName') or ''
    typ = e.get('typeName') or (e.get('type') or {}).get('typeName') or ''
    return (cat, str(fam), str(typ))


def distance_mm(a: Tuple[float, float, float], b: Tuple[float, float, float]) -> float:
    dx = a[0] - b[0]
    dy = a[1] - b[1]
    dz = a[2] - b[2]
    return (dx*dx + dy*dy + dz*dz) ** 0.5


def pick_pairs_and_unmatched(left: List[Dict[str, Any]], right: List[Dict[str, Any]], pos_tol: float = 600.0, len_tol: float = 150.0):
    # Greedy nearest-neighbor pairing by centroid; return matched pairs and unmatched buckets
    right_used = [False] * len(right)
    pairs: List[Tuple[Dict[str, Any], Dict[str, Any]]] = []
    unmatched_left: List[Dict[str, Any]] = []
    unmatched_right: List[Dict[str, Any]] = []

    L = [(i, centroid_mm(e), approx_length_mm(e)) for i, e in enumerate(left)]
    R = [(i, centroid_mm(e), approx_length_mm(e)) for i, e in enumerate(right)]

    for li, lc, ll in L:
        best = None
        bestj = -1
        for rj, rc, rl in R:
            if right_used[rj]:
                continue
            d = distance_mm(lc, rc)
            if d <= pos_tol:
                if ll > 0 and rl > 0 and abs(ll - rl) > len_tol:
                    continue
                if best is None or d < best:
                    best = d
                    bestj = rj
        if bestj >= 0:
            right_used[bestj] = True
            pairs.append((left[li], right[bestj]))
        else:
            unmatched_left.append(left[li])

    for rj, used in enumerate(right_used):
        if not used:
            unmatched_right.append(right[rj])

    return pairs, unmatched_left, unmatched_right


def get_cat_id(e: Dict[str, Any]) -> int:
    cat = e.get('categoryId')
    if cat is None and isinstance(e.get('category'), dict):
        cat = e['category'].get('id')
    try:
        return int(cat) if cat is not None else 0
    except Exception:
        return 0


def get_type_id(e: Dict[str, Any]) -> Optional[int]:
    tid = e.get('typeId')
    try:
        return int(tid) if tid is not None else None
    except Exception:
        return None


def get_type_param_value(typedict: Dict[str, Any], type_id: Optional[int], key: str) -> Optional[str]:
    if type_id is None:
        return None
    item = typedict.get(str(type_id)) or typedict.get(type_id)
    if not isinstance(item, dict):
        return None
    # params might be nested in 'params' or 'display'
    params = item.get('params') or {}
    display = item.get('display') or {}
    if key in params:
        try:
            v = params[key]
            return str(v) if v is not None else None
        except Exception:
            pass
    if key in display:
        try:
            v = display[key]
            return str(v) if v is not None else None
        except Exception:
            pass
    return None


def main():
    if len(sys.argv) < 3:
        print("Usage: strict_crossport_diff.py <left.json> <right.json> [--csv <out.csv>] [--left-ids <out.json>] [--right-ids <out.json>]", file=sys.stderr)
        sys.exit(2)
    left_path = sys.argv[1]
    right_path = sys.argv[2]
    csv_out = None
    left_ids_out = None
    right_ids_out = None
    pairs_out = None
    i = 3
    pos_tol = 600.0
    len_tol = 150.0
    keys = ['familyName','typeName','符号','H','B','tw','tf']
    while i < len(sys.argv):
        if sys.argv[i] == '--csv' and i+1 < len(sys.argv):
            csv_out = sys.argv[i+1]; i += 2
        elif sys.argv[i] == '--left-ids' and i+1 < len(sys.argv):
            left_ids_out = sys.argv[i+1]; i += 2
        elif sys.argv[i] == '--right-ids' and i+1 < len(sys.argv):
            right_ids_out = sys.argv[i+1]; i += 2
        elif sys.argv[i] == '--pairs-out' and i+1 < len(sys.argv):
            pairs_out = sys.argv[i+1]; i += 2
        elif sys.argv[i] == '--pos-tol-mm' and i+1 < len(sys.argv):
            try:
                pos_tol = float(sys.argv[i+1]); i += 2
            except Exception:
                i += 2
        elif sys.argv[i] == '--len-tol-mm' and i+1 < len(sys.argv):
            try:
                len_tol = float(sys.argv[i+1]); i += 2
            except Exception:
                i += 2
        elif sys.argv[i] == '--keys' and i+1 < len(sys.argv):
            try:
                keys = [s.strip() for s in sys.argv[i+1].split(',') if s.strip()]
            except Exception:
                pass
            i += 2
        else:
            i += 1

    left = load(left_path)
    right = load(right_path)
    lelems = left.get('elements') or []
    relems = right.get('elements') or []
    ltypes = left.get('typeParameters') or {}
    rtypes = right.get('typeParameters') or {}

    # First, prefer pairing by elementId when both sides reference the same document.
    def uid_of(e: Dict[str, Any]) -> Optional[str]:
        u = e.get('uniqueId') or (e.get('id') if isinstance(e.get('id'), str) else None)
        if isinstance(u, str) and u:
            return u
        return None

    def eid_of(e: Dict[str, Any]) -> Optional[int]:
        try:
            return int(e.get('elementId') or e.get('id'))
        except Exception:
            return None

    # Prefer matching by uniqueId (stable across sessions), fallback to elementId
    LMAP_U: Dict[str, Dict[str, Any]] = {}
    RMAP_U: Dict[str, Dict[str, Any]] = {}
    for e in lelems:
        u = uid_of(e)
        if u is not None:
            LMAP_U[u] = e
    for e in relems:
        u = uid_of(e)
        if u is not None:
            RMAP_U[u] = e

    common_uids = set(LMAP_U.keys()) & set(RMAP_U.keys())
    pairs_id: List[Tuple[Dict[str, Any], Dict[str, Any]]] = [(LMAP_U[u], RMAP_U[u]) for u in common_uids]

    # Fallback: elementId matching for those not matched by uniqueId
    L_rest_e = [e for e in lelems if uid_of(e) not in common_uids]
    R_rest_e = [e for e in relems if uid_of(e) not in common_uids]

    LMAP_E: Dict[int, Dict[str, Any]] = {}
    RMAP_E: Dict[int, Dict[str, Any]] = {}
    for e in L_rest_e:
        i = eid_of(e)
        if i is not None:
            LMAP_E[i] = e
    for e in R_rest_e:
        i = eid_of(e)
        if i is not None:
            RMAP_E[i] = e
    common_eids = set(LMAP_E.keys()) & set(RMAP_E.keys())
    pairs_id.extend([(LMAP_E[i], RMAP_E[i]) for i in common_eids])

    # Reduce the pools for geometry pairing by removing id-matched elements
    matched_uid_set = set(common_uids)
    matched_eid_set = set(common_eids)
    def matched(e: Dict[str, Any]) -> bool:
        u = uid_of(e)
        if u in matched_uid_set:
            return True
        i = eid_of(e)
        return i in matched_eid_set if i is not None else False

    lelems_rest = [e for e in lelems if not matched(e)]
    relems_rest = [e for e in relems if not matched(e)]

    # Group by (cat,fam,typ)
    from collections import defaultdict
    GL = defaultdict(list)
    GR = defaultdict(list)
    for e in lelems_rest:
        GL[elem_group_key(e)].append(e)
    for e in relems_rest:
        GR[elem_group_key(e)].append(e)

    # First pass: geometry pairing across ALL groups (to catch type/param changes)
    pairs_all, _, _ = pick_pairs_and_unmatched(lelems_rest, relems_rest, pos_tol=pos_tol, len_tol=len_tol)

    # Classify pairs into "modified" vs "same" (by family/type and selected params)
    def get_param_map(e: Dict[str, Any], typedict: Dict[str, Any]) -> Dict[str, str]:
        res = {}
        for p in e.get('parameters') or []:
            try:
                name = p.get('name'); val = p.get('display') if p.get('display') is not None else p.get('value')
                if name:
                    res[str(name)] = str(val)
            except Exception:
                continue
        # add type-level keys if missing
        tid = get_type_id(e)
        for k in keys:
            if k in ('familyName','typeName'):
                continue
            if k not in res:
                tv = get_type_param_value(typedict, tid, k)
                if tv is not None:
                    res[k] = tv
        return res

    modified_left: List[Dict[str, Any]] = []
    modified_right: List[Dict[str, Any]] = []
    modified_pairs: List[Dict[str, Any]] = []

    same_pairs: List[Tuple[Dict[str, Any], Dict[str, Any]]] = []

    # 0) Classify id-matched pairs first
    for a, b in pairs_id:
        fam_a, typ_a = str(a.get('familyName') or ''), str(a.get('typeName') or '')
        fam_b, typ_b = str(b.get('familyName') or ''), str(b.get('typeName') or '')
        try:
            tid_a = int(a.get('typeId')) if a.get('typeId') is not None else None
        except Exception:
            tid_a = None
        try:
            tid_b = int(b.get('typeId')) if b.get('typeId') is not None else None
        except Exception:
            tid_b = None
        pm_a, pm_b = get_param_map(a, ltypes), get_param_map(b, rtypes)
        diffs = []
        if fam_a != fam_b:
            diffs.append({'key': 'familyName', 'left': fam_a, 'right': fam_b})
        if typ_a != typ_b:
            diffs.append({'key': 'typeName', 'left': typ_a, 'right': typ_b})
        if tid_a is not None and tid_b is not None and tid_a != tid_b:
            diffs.append({'key': 'typeId', 'left': tid_a, 'right': tid_b})
        for k in keys:
            if k in ('familyName','typeName'):
                continue
            va = pm_a.get(k); vb = pm_b.get(k)
            if va is not None and vb is not None and str(va) != str(vb):
                diffs.append({'key': k, 'left': str(va), 'right': str(vb)})
        if diffs:
            modified_left.append(a)
            modified_right.append(b)
            modified_pairs.append({
                'leftId': int(a.get('elementId') or 0),
                'rightId': int(b.get('elementId') or 0),
                'leftCatId': get_cat_id(a),
                'rightCatId': get_cat_id(b),
                'diffs': diffs
            })
        else:
            same_pairs.append((a, b))
    # 1) Classify geometry-matched pairs for the remaining elements
    for a, b in pairs_all:
        fam_a, typ_a = str(a.get('familyName') or ''), str(a.get('typeName') or '')
        fam_b, typ_b = str(b.get('familyName') or ''), str(b.get('typeName') or '')
        try:
            tid_a = int(a.get('typeId')) if a.get('typeId') is not None else None
        except Exception:
            tid_a = None
        try:
            tid_b = int(b.get('typeId')) if b.get('typeId') is not None else None
        except Exception:
            tid_b = None
        pm_a, pm_b = get_param_map(a, ltypes), get_param_map(b, rtypes)
        diffs = []
        # family/type also checked
        if fam_a != fam_b:
            diffs.append({'key':'familyName','left':fam_a,'right':fam_b})
        if typ_a != typ_b:
            diffs.append({'key':'typeName','left':typ_a,'right':typ_b})
        if tid_a is not None and tid_b is not None and tid_a != tid_b:
            diffs.append({'key':'typeId','left':tid_a,'right':tid_b})
        for k in keys:
            if k in ('familyName','typeName'):
                continue
            va = pm_a.get(k); vb = pm_b.get(k)
            if va is not None and vb is not None and str(va) != str(vb):
                diffs.append({'key':k,'left':str(va),'right':str(vb)})
        if diffs:
            modified_left.append(a)
            modified_right.append(b)
            modified_pairs.append({
                'leftId': int(a.get('elementId') or 0),
                'rightId': int(b.get('elementId') or 0),
                'leftCatId': get_cat_id(a),
                'rightCatId': get_cat_id(b),
                'diffs': diffs
            })
        else:
            same_pairs.append((a, b))

    # Remove all matched ids from both sides before computing unmatched by groups
    matched_left_ids = set(int(a.get('elementId') or 0) for a, _ in pairs_all) | set(int(a.get('elementId') or 0) for a, _ in pairs_id)
    matched_right_ids = set(int(b.get('elementId') or 0) for _, b in pairs_all) | set(int(b.get('elementId') or 0) for _, b in pairs_id)

    # Second pass: within each group, pick remaining unmatched (e.g., count differences not caught by pairing)
    keys = set(GL.keys()) | set(GR.keys())
    left_only: List[Dict[str, Any]] = []
    right_only: List[Dict[str, Any]] = []
    for k in keys:
        # Filter out elements that were already matched globally to avoid double-counting
        L = [e for e in GL.get(k, []) if int(e.get('elementId') or 0) not in matched_left_ids]
        R = [e for e in GR.get(k, []) if int(e.get('elementId') or 0) not in matched_right_ids]
        if not L and not R:
            continue
        if not L:
            right_only.extend(R)
            continue
        if not R:
            left_only.extend(L)
            continue
        pairs, ul, ur = pick_pairs_and_unmatched(L, R, pos_tol=pos_tol, len_tol=len_tol)
        left_only.extend(ul)
        right_only.extend(ur)

    # Write IDs JSON if requested
    if left_ids_out:
        with open(left_ids_out, 'w', encoding='utf-8') as f:
            json.dump([int(e.get('elementId') or e.get('id')) for e in left_only + modified_left if e.get('elementId') or e.get('id')], f, ensure_ascii=False)
    if right_ids_out:
        with open(right_ids_out, 'w', encoding='utf-8') as f:
            json.dump([int(e.get('elementId') or e.get('id')) for e in right_only + modified_right if e.get('elementId') or e.get('id')], f, ensure_ascii=False)

    if pairs_out:
        with open(pairs_out, 'w', encoding='utf-8') as f:
            json.dump(modified_pairs, f, ensure_ascii=False, indent=2)

    # CSV summary (optional)
    if csv_out:
        import csv
        Path(csv_out).parent.mkdir(parents=True, exist_ok=True)
        with open(csv_out, 'w', newline='', encoding='utf-8') as f:
            w = csv.writer(f)
            w.writerow(['side','elementId','categoryId','familyName','typeName','cx','cy','cz','note'])
            for side, arr in [('left', left_only), ('right', right_only)]:
                for e in arr:
                    eid = int(e.get('elementId') or e.get('id') or 0)
                    cat = int(e.get('categoryId') or (e.get('category') or {}).get('id') or 0)
                    fam = e.get('familyName') or (e.get('type') or {}).get('familyName') or ''
                    typ = e.get('typeName') or (e.get('type') or {}).get('typeName') or ''
                    cx,cy,cz = centroid_mm(e)
                    w.writerow([side, eid, cat, fam, typ, f"{cx:.1f}", f"{cy:.1f}", f"{cz:.1f}", 'unmatched'])
            # modified rows
            for side, arr in [('left', modified_left), ('right', modified_right)]:
                for e in arr:
                    eid = int(e.get('elementId') or e.get('id') or 0)
                    cat = int(e.get('categoryId') or (e.get('category') or {}).get('id') or 0)
                    fam = e.get('familyName') or (e.get('type') or {}).get('familyName') or ''
                    typ = e.get('typeName') or (e.get('type') or {}).get('typeName') or ''
                    cx,cy,cz = centroid_mm(e)
                    w.writerow([side, eid, cat, fam, typ, f"{cx:.1f}", f"{cy:.1f}", f"{cz:.1f}", 'modified'])

    print(json.dumps({
        'ok': True,
        'leftOnlyCount': len(left_only),
        'rightOnlyCount': len(right_only),
        'leftModifiedCount': len(modified_left),
        'rightModifiedCount': len(modified_right)
    }, ensure_ascii=False))


if __name__ == '__main__':
    main()
