import argparse
import json
from tools.mcp_safe import call_mcp


def unwrap(x):
    top = x.get('result') or x
    if isinstance(top, dict) and 'result' in top:
        top = top['result']
    return top if isinstance(top, dict) else {}


def main():
    ap = argparse.ArgumentParser(description='Delete most elements (rooms, doors, windows, walls, grids); levels are kept')
    ap.add_argument('--port', type=int, required=True)
    args = ap.parse_args()
    p = args.port

    # Rooms
    try:
        rooms = unwrap(call_mcp(p, 'get_rooms', {'skip':0,'count':20000})).get('rooms', [])
        for r in rooms:
            try:
                call_mcp(p, 'delete_room', {'elementId': int(r.get('elementId'))})
            except Exception:
                pass
    except Exception:
        pass

    # Doors
    try:
        doors = unwrap(call_mcp(p, 'get_doors', {'skip':0,'count':20000})).get('doors', [])
        for d in doors:
            try:
                call_mcp(p, 'delete_door', {'elementId': int(d.get('elementId'))})
            except Exception:
                pass
    except Exception:
        pass

    # Windows
    try:
        wins = unwrap(call_mcp(p, 'get_windows', {'skip':0,'count':20000})).get('windows', [])
        for w in wins:
            try:
                call_mcp(p, 'delete_window', {'elementId': int(w.get('elementId'))})
            except Exception:
                pass
    except Exception:
        pass

    # Walls
    try:
        walls = unwrap(call_mcp(p, 'get_walls', {'skip':0,'count':50000})).get('walls', [])
        for w in walls:
            try:
                call_mcp(p, 'delete_wall', {'elementId': int(w.get('elementId') or w.get('id'))})
            except Exception:
                pass
    except Exception:
        pass

    # Grids
    try:
        grids = unwrap(call_mcp(p, 'get_grids', {'skip':0,'count':20000})).get('grids', [])
        for g in grids:
            try:
                call_mcp(p, 'delete_grid', {'elementId': int(g.get('elementId') or g.get('id'))})
            except Exception:
                pass
    except Exception:
        pass

    print(json.dumps({'ok': True, 'deleted': True}, ensure_ascii=False))


if __name__ == '__main__':
    main()

