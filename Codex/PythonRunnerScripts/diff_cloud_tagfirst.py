#!/usr/bin/env python
# -*- coding: utf-8 -*-
# @feature: diff cloud tagfirst | keywords: ビュー, タグ, キャプチャ, スナップショット

"""
Diff + Tag-first Revision Cloud script (client-side orchestrator).

- Compares current view vs. a baseline snapshot (per-view JSON),
  then creates revision clouds for Added/Modified elements.
- If a tag exists for a changed element, optionally place cloud on the tag
  (ask/prefer/never) using tag width/height from get_tag_bounds_in_view.
- Skips existing RevisionCloud elements (no cloud-on-cloud).
- Writes CSV with elementId, category, diff summary (Added/Removed/Modified[:paramNames]).
- Optional: write the diff summary into cloud's Comments parameter.

Usage:
  python Scripts/Reference/diff_cloud_tagfirst.py --port 5210 \
      --baseline Projects/Project_5210_B/20251027_123122 \
      --tag-mode prefer --write-comments --csv Projects/DiffCloud/out.csv
"""

import argparse
import csv
import json
import os
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
SEND = HERE / 'send_revit_command_durable.py'


def run(port, method, params=None, force=True, wait=120, timeout=600):
    args = [sys.executable, '-X', 'utf8', str(SEND), '--port', str(port), '--command', method,
            '--wait-seconds', str(wait), '--timeout-sec', str(timeout)]
    if force:
        args.append('--force')
    if params:
        args += ['--params', json.dumps(params, ensure_ascii=False)]
    p = subprocess.run(args, capture_output=True, text=True, encoding='utf-8')
    out = p.stdout.strip()
    if p.returncode != 0:
        raise RuntimeError(f'{method} failed: rc={p.returncode}\n{out}\n{p.stderr}')
    try:
        return json.loads(out)
    except Exception:
        return {'ok': False, 'raw': out}


def get_result_payload(obj):
    if isinstance(obj, dict):
        if 'result' in obj and isinstance(obj['result'], dict):
            r = obj['result'].get('result')
            if r is not None:
                return r
            return obj['result']
        return obj
    return {}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--port', type=int, required=True)
    ap.add_argument('--baseline', required=True, help='Baseline dir containing view_<id>_elements.json')
    ap.add_argument('--tag-mode', choices=['ask', 'prefer', 'never'], default='ask')
    ap.add_argument('--write-comments', action='store_true')
    ap.add_argument('--csv', default='diff_report.csv')
    ap.add_argument('--padding-mm', type=float, default=150.0)
    args = ap.parse_args()

    port = args.port
    baseline_dir = Path(args.baseline)
    tag_mode = args.tag_mode
    write_comments = args.write_comments
    padding = args.padding_mm

    # 1) current view
    cur = get_result_payload(run(port, 'get_current_view'))
    view_id = int(cur.get('viewId'))

    # 2) baseline per-view json
    base_file = baseline_dir / f'view_{view_id}_elements.json'
    if not base_file.exists():
        print(f'Baseline not found: {base_file}', file=sys.stderr)
        sys.exit(2)
    with base_file.open('r', encoding='utf-8') as f:
        base = json.load(f)
    base_elems = base.get('elements') or []

    # 3) tags in view
    tags = get_result_payload(run(port, 'get_tags_in_view', {'viewId': view_id})).get('tags') or []
    tags_by_host = {}
    for t in tags:
        hid = int(t.get('hostElementId') or t.get('hostId') or 0)
        tid = int(t.get('tagId') or t.get('elementId') or t.get('id') or 0)
        if hid and tid:
            tags_by_host.setdefault(hid, []).append(tid)

    # 4) now ids + details
    now_ids = get_result_payload(run(port, 'get_elements_in_view', {'viewId': view_id, '_shape': {'idsOnly': True}}, wait=180, timeout=600)).get('elementIds') or []
    now_elems = []
    CHUNK = 200
    for i in range(0, len(now_ids), CHUNK):
        part = now_ids[i:i+CHUNK]
        r = get_result_payload(run(port, 'get_element_info', {'elementIds': part, 'rich': True}, wait=300, timeout=900))
        now_elems.extend(r.get('elements') or r.get('result', {}).get('elements') or [])

    # 5) maps
    map_now = {int(e.get('elementId')): e for e in now_elems if e.get('elementId') is not None}
    map_base = {int(e.get('elementId')): e for e in base_elems if e.get('elementId') is not None}
    now_set = set(map_now.keys())
    base_set = set(map_base.keys())

    # exclude revision clouds themselves (no cloud-on-cloud)
    def is_revision_cloud(e):
        cat = (e.get('category') or e.get('categoryName') or '').lower()
        cls = (e.get('className') or '').lower()
        return 'revision' in cat and 'cloud' in cat or 'revisioncloud' in cls

    now_set = {eid for eid in now_set if not is_revision_cloud(map_now.get(eid, {}))}

    added = sorted(eid for eid in now_set if eid not in base_set)
    removed = sorted(eid for eid in base_set if eid not in now_set)
    common = sorted(eid for eid in now_set if eid in base_set)

    modified = []
    changed_params = {}
    for eid in common:
        a = map_base[eid]
        b = map_now[eid]
        aj = json.dumps(a, ensure_ascii=False, separators=(',', ':'), default=str)
        bj = json.dumps(b, ensure_ascii=False, separators=(',', ':'), default=str)
        if aj != bj:
            modified.append(eid)
            pA = {p.get('name'): str(p.get('display') or p.get('value')) for p in (a.get('parameters') or []) if p and p.get('name')}
            pB = {p.get('name'): str(p.get('display') or p.get('value')) for p in (b.get('parameters') or []) if p and p.get('name')}
            keys = sorted(set(pA.keys()) | set(pB.keys()))
            diffs = [k for k in keys if pA.get(k) != pB.get(k)]
            changed_params[eid] = diffs[:5]

    # 6) revision id
    rev = get_result_payload(run(port, 'list_revisions'))
    rev_items = rev.get('revisions') or []
    rev_id = int(rev_items[-1]['id']) if rev_items else int(get_result_payload(run(port, 'create_default_revision')).get('revisionId'))

    # 7) cloud pass (tag-first)
    created_clouds = []
    def cloud_on_tag(tag_id):
        b = get_result_payload(run(port, 'get_tag_bounds_in_view', {'viewId': view_id, 'tagId': int(tag_id), 'inflateMm': 100}))
        wmm = float(b.get('widthMm') or 0.0)
        hmm = float(b.get('heightMm') or 0.0)
        pr = {'viewId': view_id, 'revisionId': rev_id, 'elementId': int(tag_id), 'paddingMm': 120,
              'tagWidthMm': wmm, 'tagHeightMm': hmm}
        r = get_result_payload(run(port, 'create_revision_cloud_for_element_projection', pr))
        if r and r.get('ok'):
            cid = r.get('cloudId')
            if cid:
                created_clouds.append(int(cid))
                return True
        return False

    def cloud_on_element(eid):
        pr = {'viewId': view_id, 'revisionId': rev_id, 'elementId': int(eid), 'paddingMm': padding}
        r = get_result_payload(run(port, 'create_revision_cloud_for_element_projection', pr))
        if r and r.get('ok') and r.get('cloudId'):
            created_clouds.append(int(r['cloudId']))
            return True
        # fallback: bbox rectangle
        eNow = map_now.get(eid)
        bb = eNow.get('boundingBox') or {}
        bbmm = eNow.get('bboxMm') or {}
        def rect_from(bbft=None, bbmm=None):
            if bbft and bbft.get('min') and bbft.get('max'):
                ft2mm = 304.8
                x0 = float(bbft['min']['x'])*ft2mm - 100
                y0 = float(bbft['min']['y'])*ft2mm - 100
                x1 = float(bbft['max']['x'])*ft2mm + 100
                y1 = float(bbft['max']['y'])*ft2mm + 100
                return x0,y0,x1,y1
            if bbmm and bbmm.get('min') and bbmm.get('max'):
                x0 = float(bbmm['min']['x']) - 100
                y0 = float(bbmm['min']['y']) - 100
                x1 = float(bbmm['max']['x']) + 100
                y1 = float(bbmm['max']['y']) + 100
                return x0,y0,x1,y1
            return None
        rect = rect_from(bb, bbmm)
        if not rect:
            return False
        x0,y0,x1,y1 = rect
        loop = [
            {'start': {'x':x0,'y':y0,'z':0}, 'end': {'x':x1,'y':y0,'z':0}},
            {'start': {'x':x1,'y':y0,'z':0}, 'end': {'x':x1,'y':y1,'z':0}},
            {'start': {'x':x1,'y':y1,'z':0}, 'end': {'x':x0,'y':y1,'z':0}},
            {'start': {'x':x0,'y':y1,'z':0}, 'end': {'x':x0,'y':y0,'z':0}},
        ]
        pr2 = {'viewId': view_id, 'revisionId': rev_id, 'curveLoops': [loop]}
        r2 = get_result_payload(run(port, 'create_revision_cloud', pr2, wait=180, timeout=600))
        return bool(r2 and r2.get('ok'))

    targets = sorted(set(added + modified))
    for eid in targets:
        # ask/prefer/never tag mode
        used_tag = False
        if tag_mode in ('ask','prefer') and eid in tags_by_host:
            tag_id = tags_by_host[eid][0]
            if tag_mode == 'ask':
                ans = input(f'Element {eid}: tag {tag_id} にクラウドを付けますか？ [y/N]: ').strip().lower()
                if ans == 'y':
                    used_tag = cloud_on_tag(tag_id)
            else:
                used_tag = cloud_on_tag(tag_id)
        if not used_tag:
            cloud_on_element(eid)

    # 8) CSV
    csv_path = Path(args.csv)
    csv_path.parent.mkdir(parents=True, exist_ok=True)
    with csv_path.open('w', newline='', encoding='utf-8') as f:
        w = csv.writer(f)
        w.writerow(['elementId','category','diff'])
        for eid in added:
            e = map_now.get(eid)
            w.writerow([eid, (e.get('category') or e.get('categoryName') or ''), '追加'])
        for eid in removed:
            e = map_base.get(eid)
            w.writerow([eid, (e.get('category') or e.get('categoryName') or ''), '削除'])
        for eid in modified:
            e = map_now.get(eid)
            labels = changed_params.get(eid) or []
            desc = '変更' + (': ' + '/'.join(labels) if labels else '')
            w.writerow([eid, (e.get('category') or e.get('categoryName') or ''), desc])

    # 9) write comments (optional)
    if write_comments and created_clouds:
        for eid in modified:
            labels = changed_params.get(eid) or []
            if not labels:
                continue
            # best-effort: find a cloud we just created last; set generic comment
            comment = f'変更: {"/".join(labels[:5])}'
            # skip if no cloud id resolution; (optionally list clouds in view and match by proximity)
            # here we simply set on the most recent cloud
            cid = created_clouds[-1]
            try:
                run(port, 'set_revision_cloud_parameter', {'elementId': int(cid), 'name': 'コメント', 'value': comment}, wait=60, timeout=120)
            except Exception:
                try:
                    run(port, 'set_revision_cloud_parameter', {'elementId': int(cid), 'name': 'Comments', 'value': comment}, wait=60, timeout=120)
                except Exception:
                    pass

    print(json.dumps({'ok': True, 'csv': str(csv_path), 'cloudsCreated': len(created_clouds)}, ensure_ascii=False))


if __name__ == '__main__':
    main()





