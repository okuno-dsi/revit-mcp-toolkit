import json
import sys
from collections import Counter, defaultdict
from pathlib import Path

def load(path: str):
    p = Path(path)
    with p.open('r', encoding='utf-8') as f:
        return json.load(f)

def key_of(elem):
    cat = elem.get('categoryId') or elem.get('category', {}).get('id')
    fam = elem.get('familyName') or (elem.get('type') or {}).get('familyName')
    typ = elem.get('typeName') or (elem.get('type') or {}).get('typeName')
    return (int(cat) if cat is not None else None, fam or '', typ or '')

def make_summary(data: dict):
    elems = data.get('elements') or []
    cnt = Counter()
    for e in elems:
        cnt[key_of(e)] += 1
    return cnt

def diff_counts(left: Counter, right: Counter):
    all_keys = set(left) | set(right)
    diffs = []
    for k in sorted(all_keys):
        lv = left.get(k, 0)
        rv = right.get(k, 0)
        if lv != rv:
            diffs.append((k, lv, rv, rv - lv))
    return diffs

def format_key(k):
    cat, fam, typ = k
    return f"cat={cat} | family={fam} | type={typ}"

def main():
    if len(sys.argv) != 3:
        print("Usage: compare_structural_details.py <left.json> <right.json>")
        sys.exit(2)
    left = load(sys.argv[1])
    right = load(sys.argv[2])
    lc = make_summary(left)
    rc = make_summary(right)
    diffs = diff_counts(lc, rc)
    if not diffs:
        print("No differences in (categoryId, familyName, typeName) counts.")
        return
    print("Differences (Left -> Right):")
    for k, lv, rv, delta in diffs[:200]:
        print(f"- {format_key(k)}: {lv} -> {rv} (Δ {delta:+d})")

if __name__ == '__main__':
    main()

