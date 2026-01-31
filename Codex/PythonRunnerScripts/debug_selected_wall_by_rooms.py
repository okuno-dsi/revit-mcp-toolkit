# @feature: debug selected wall by rooms | keywords: 壁, 部屋, 集計表, レベル
import json
import math
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, List, Tuple


def call_revit(command: str, params: Dict[str, Any], port: int = 5210) -> Dict[str, Any]:
    args = [
        sys.executable,
        "send_revit_command_durable.py",
        "--port",
        str(port),
        "--command",
        command,
        "--params",
        json.dumps(params, ensure_ascii=False),
    ]
    out = subprocess.check_output(args, text=True)
    data = json.loads(out)
    return data["result"]["result"]


@dataclass
class WallInfo:
    element_id: int
    type_name: str
    level_id: int
    start: Tuple[float, float, float]
    end: Tuple[float, float, float]
    height: float


def get_selected_wall(port: int = 5210) -> WallInfo:
    sel = call_revit("get_selected_element_ids", {}, port=port)
    ids = sel.get("elementIds") or []
    if not ids:
        raise SystemExit("選択されている要素がありません。")
    if len(ids) > 1:
        print(f"警告: {len(ids)} 個の要素が選択されています。先頭のみを使用します。", file=sys.stderr)
    wall_id = int(ids[0])

    walls_res = call_revit("get_walls", {"skip": 0, "count": 100000}, port=port)
    walls = walls_res.get("walls", [])
    for w in walls:
        if int(w.get("elementId")) == wall_id:
            start = w["start"]
            end = w["end"]
            return WallInfo(
                element_id=wall_id,
                type_name=w.get("typeName") or "",
                level_id=int(w.get("levelId") or 0),
                start=(float(start["x"]), float(start["y"]), float(start["z"])),
                end=(float(end["x"]), float(end["y"]), float(end["z"])),
                height=float(w.get("height") or 0.0),
            )
    raise SystemExit(f"get_walls の結果に elementId={wall_id} が見つかりませんでした。")


def get_rooms(port: int = 5210) -> List[Dict[str, Any]]:
    res = call_revit("get_rooms", {"skip": 0, "count": 10000}, port=port)
    return res.get("rooms", [])


def main() -> None:
    port = 5210
    wall = get_selected_wall(port=port)
    rooms = get_rooms(port=port)

    print(f"選択中の壁 ID: {wall.element_id}")
    print(f"  typeName: {wall.type_name}")
    print(f"  levelId : {wall.level_id}")

    if wall.height <= 0:
        print("  height が 0 以下のため、サンプル高さを推定できません。", file=sys.stderr)

    # サンプル点（壁芯の高さは start.z + height/2 とする）
    z_mm = wall.start[2] + (wall.height * 0.5 if wall.height > 0 else 0.0)

    dx = wall.end[0] - wall.start[0]
    dy = wall.end[1] - wall.start[1]
    length = math.hypot(dx, dy)
    if length < 1e-3:
        raise SystemExit("壁の長さが非常に短いため解析できません。")

    dirx = dx / length
    diry = dy / length

    # 2D で垂直方向ベクトル（左右）
    nx = -diry
    ny = dirx

    offset_mm = 1000.0  # C# 側と同様に 1m 程度

    # t=10%,30%,50%,70%,90% の 5 点をサンプリング
    ts = [0.1, 0.3, 0.5, 0.7, 0.9]

    points: List[List[float]] = []
    side_flags: List[str] = []  # "A" or "B" 側

    for t in ts:
        cx = wall.start[0] + dx * t
        cy = wall.start[1] + dy * t
        # 側 A, 側 B （どちらが室内/室外かはここでは未定）
        ax = cx + nx * offset_mm
        ay = cy + ny * offset_mm
        bx = cx - nx * offset_mm
        by = cy - ny * offset_mm

        points.append([round(ax, 3), round(ay, 3), round(z_mm, 3)])
        side_flags.append(f"A@{t:.2f}")
        points.append([round(bx, 3), round(by, 3), round(z_mm, 3)])
        side_flags.append(f"B@{t:.2f}")

    # ポイント座標 → インデックス
    coord_to_idx: Dict[Tuple[float, float, float], int] = {
        (p[0], p[1], p[2]): i for i, p in enumerate(points)
    }

    # 各ポイントがどの部屋に属するか
    point_rooms: List[List[str]] = [[] for _ in points]

    for r in rooms:
        rid = int(r.get("elementId") or 0)
        rname = r.get("name") or ""
        if rid <= 0:
            continue

        # この部屋に対して全ポイントを判定
        cmd_res = call_revit(
            "classify_points_in_room",
            {
                "roomId": rid,
                "points": points,
            },
            port=port,
        )
        inside = cmd_res.get("inside") or []
        for arr in inside:
            x, y, z = float(arr[0]), float(arr[1]), float(arr[2])
            key = (round(x, 3), round(y, 3), round(z, 3))
            idx = coord_to_idx.get(key)
            if idx is not None:
                point_rooms[idx].append(rname or f"Room[{rid}]")

    # 結果の集計
    side_stats: Dict[str, Dict[str, Any]] = {}
    for idx, (pt, side) in enumerate(zip(points, side_flags)):
        rooms_here = point_rooms[idx]
        key = side.split("@")[0]  # "A" or "B"
        stat = side_stats.setdefault(key, {"count": 0, "withRoom": 0, "rooms": set()})
        stat["count"] += 1
        if rooms_here:
            stat["withRoom"] += 1
            for rn in rooms_here:
                stat["rooms"].add(rn)

        print(
            f"Point {idx:02d} side={side}: ({pt[0]:.1f},{pt[1]:.1f},{pt[2]:.1f}) "
            f"rooms={rooms_here if rooms_here else '[]'}"
        )

    print("\n====== Summary by side ======")
    for side in sorted(side_stats.keys()):
        stat = side_stats[side]
        rooms_list = sorted(stat["rooms"])
        print(
            f"Side {side}: samples={stat['count']}, "
            f"withRoom={stat['withRoom']}, rooms={rooms_list}"
        )


if __name__ == "__main__":
    main()

