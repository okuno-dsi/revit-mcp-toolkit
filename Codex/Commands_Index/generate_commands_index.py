#!/usr/bin/env python3
"""
Generate Manuals/Commands/commands_index.json from a list of available command names
and existing index, assigning heuristic metadata for new commands.

Usage:
  python Commands_Index/generate_commands_index.py --names Work/<Project>_<Port>/Logs/list_commands_names.json \
      --out Manuals/Commands/commands_index.json

Notes:
  - --names can be:
      * JSON array of names
      * JSON-RPC result object containing names at result.result.commands or result.commands
      * If omitted, script will try to reuse existing commands_index.json keys.
  - Existing entries are preserved; only missing commands are added.
"""
import argparse
import json
import os
import sys
from glob import glob


DEFAULT_OUT = os.path.join('Manuals', 'Commands', 'commands_index.json')


def load_names(path: str):
    with open(path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    # Accept array
    if isinstance(data, list):
        return [str(x) for x in data]
    # Accept RPC shapes
    if isinstance(data, dict):
        # result.result.commands
        cmds = None
        try:
            cmds = data.get('result', {}).get('result', {}).get('commands')
        except Exception:
            cmds = None
        if not cmds:
            try:
                cmds = data.get('result', {}).get('commands')
            except Exception:
                cmds = None
        if not cmds:
            # direct
            cmds = data.get('commands')
        if isinstance(cmds, list):
            return [str(x) for x in cmds]
    raise ValueError('Unrecognized names file format: {}'.format(path))


def guess_kind(name: str) -> str:
    n = name.lower()
    if n.startswith(('get_', 'list_', 'search_', 'describe_', 'export_', 'ping_', 'agent_', 'debug_', 'preview_', 'summarize_')):
        return 'read'
    return 'write'


def guess_category(name: str) -> str:
    n = name.lower()
    if 'view' in n or 'visual' in n or 'colorfill' in n:
        # UI windowing ops go to RevitUI; data-level go to Views/Graphics
        if any(k in n for k in ('activate_view', 'open_views', 'arrange', 'tile_windows', 'dockable')):
            return 'RevitUI'
        return 'Views/Graphics'
    if 'sheet' in n:
        return 'Sheets'
    if 'level' in n:
        return 'Levels'
    if 'grid' in n:
        return 'Grids'
    if 'room' in n or 'space' in n:
        return 'Rooms/Spaces'
    if 'workset' in n:
        return 'Worksets'
    if 'family' in n:
        return 'Families'
    if 'type' in n and 'view' not in n:
        return 'Types'
    if 'param' in n:
        return 'Parameters'
    if 'mep' in n:
        return 'MEP'
    if 'wall' in n:
        return 'Walls'
    if 'floor' in n:
        return 'Floors'
    if 'roof' in n:
        return 'Roofs'
    if 'link' in n:
        return 'Links'
    if 'revision' in n or 'cloud' in n:
        return 'Revisions'
    if 'schedule' in n:
        return 'Schedules'
    if 'export' in n:
        return 'Export'
    if 'geometry' in n:
        return 'Geometry'
    if 'project' in n or 'document' in n or 'bootstrap' in n:
        return 'Bootstrap/Project'
    return 'Other'


HIGH_SET = {
    'get_elements_in_view', 'set_visual_override', 'export_dwg', 'update_wall_parameter',
    'get_element_info', 'get_views', 'list_open_views'
}


def guess_importance(name: str) -> str:
    return 'high' if name in HIGH_SET else 'normal'


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--names', help='Path to names JSON (array or RPC result). If omitted, fall back to existing index keys or Work/*/Logs/list_commands_names*.json')
    ap.add_argument('--out', default=DEFAULT_OUT, help='Output commands_index.json path')
    args = ap.parse_args()

    # Load existing index if any
    existing = {}
    if os.path.exists(args.out):
        try:
            with open(args.out, 'r', encoding='utf-8') as f:
                existing = json.load(f)
        except Exception:
            existing = {}

    # Resolve names source
    names = []
    if args.names and os.path.exists(args.names):
        names = load_names(args.names)
    else:
        # try Work/*/Logs/list_commands_names*.json (pick the most recent)
        cands = []
        for pat in (
            os.path.join('Work', '*', 'Logs', 'list_commands_names*.json'),
            os.path.join('Work', '*_*', 'Logs', 'list_commands_names*.json'),
        ):
            cands.extend(glob(pat))
        if cands:
            cands.sort(key=lambda p: os.path.getmtime(p), reverse=True)
            try:
                names = load_names(cands[0])
            except Exception:
                names = []
        if not names and existing:
            names = sorted(existing.keys())

    if not names:
        print('No command names found. Provide --names or ensure Work/*/Logs/list_commands_names.json exists.', file=sys.stderr)
        sys.exit(2)

    index = dict(existing) if isinstance(existing, dict) else {}
    added = 0
    for name in names:
        if not name or not isinstance(name, str):
            continue
        if name in index:
            continue
        index[name] = {
            'category': guess_category(name),
            'importance': guess_importance(name),
            'kind': guess_kind(name),
        }
        added += 1

    # Write output (stable sort by key)
    # Keep existing entries for names not in the current list as-is (do not prune)
    with open(args.out, 'w', encoding='utf-8') as f:
        json.dump(index, f, ensure_ascii=False, indent=2, sort_keys=False)

    print(f'Commands index written to {args.out}. Added {added} new entries. Total {len(index)}.')


if __name__ == '__main__':
    main()

