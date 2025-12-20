import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, List, Tuple


def _add_scripts_to_path() -> None:
    here = Path(__file__).resolve().parent
    if str(here) not in sys.path:
        sys.path.insert(0, str(here))


_add_scripts_to_path()

from send_revit_command_durable import send_request, RevitMcpError  # type: ignore  # noqa: E402


def unwrap(payload: Dict[str, Any]) -> Dict[str, Any]:
    """
    send_request(...) の JSON-RPC ラッパーを剥がして、
    Revit MCP コマンドの { ok, ... } 形だけを返す。
    """
    obj: Any = payload
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict):
        return obj
    return {}


def collect_material_thermal_map(port: int) -> Tuple[Dict[int, Dict[str, Any]], Dict[int, List[int]]]:
    """
    すべてのマテリアルについて ThermalAsset を調べ、
    assetId -> ThermalConductivity, Density, SpecificHeat の代表値
    assetId -> そのアセットを参照している materialId 一覧
    を返す。
    """
    mats_env = send_request(
        port,
        "get_materials",
        {
            "_shape": {"idsOnly": False},
            "page": {"limit": 10000},
            "summaryOnly": False,
        },
    )
    mats = unwrap(mats_env)
    material_rows = mats.get("materials") or []

    asset_props: Dict[int, Dict[str, Any]] = {}
    asset_to_materials: Dict[int, List[int]] = {}

    for m in material_rows:
        mid = int(m.get("materialId") or 0)
        if mid <= 0:
            continue

        try:
            props_env = send_request(
                port,
                "get_material_asset_properties",
                {"materialId": mid, "assetKind": "thermal"},
            )
        except RevitMcpError:
            continue

        props = unwrap(props_env)
        th = props.get("thermal") or None
        if not th:
            continue

        asset_id = int(th.get("assetId") or 0)
        if asset_id <= 0:
            continue

        # properties[] の中から ThermalConductivity, Density, SpecificHeat を拾う
        lam = None
        rho = None
        cp = None
        for p in th.get("properties") or []:
            pid = p.get("id") or p.get("name")
            if pid == "ThermalConductivity":
                lam = float(p.get("value"))
            elif pid == "Density":
                rho = float(p.get("value"))
            elif pid == "SpecificHeat":
                cp = float(p.get("value"))

        # 代表値が未登録なら記録しておく（複数マテリアルが同じアセットを共有していても1つでよい）
        if asset_id not in asset_props:
            asset_props[asset_id] = {
                "assetId": asset_id,
                "name": th.get("name") or "",
                "lambda_W_per_mK": lam,
                "density_kg_per_m3": rho,
                "specificHeat_J_per_kgK": cp,
            }

        asset_to_materials.setdefault(asset_id, []).append(mid)

    return asset_props, asset_to_materials


def simple_lambda_flags(lam: float) -> List[str]:
    """
    ごく単純な閾値ベースのフラグ付け。
    建築材料のλとして「かなり怪しい」と思われる値だけ、ラベルを付ける。
    """
    flags: List[str] = []
    if lam <= 0:
        flags.append("NON_POSITIVE")
    elif lam < 0.01:
        flags.append("VERY_LOW")
    elif lam > 20.0:
        flags.append("VERY_HIGH")
    return flags


def main(argv: List[str]) -> int:
    ap = argparse.ArgumentParser(
        description=(
            "Material に紐づく ThermalAsset を走査し、熱伝導率などの一覧と、"
            "明らかに怪しい値を持つアセット候補を JSON レポートとして出力します。"
        )
    )
    ap.add_argument("--port", type=int, default=5210, help="Revit MCP ポート番号")
    ap.add_argument(
        "--output-json",
        type=str,
        default="Work/tmp_thermal_assets_audit.json",
        help="レポートを書き出す JSON パス（Codex ルート基準）",
    )
    args = ap.parse_args(argv)

    port = args.port

    try:
        asset_props, asset_to_mats = collect_material_thermal_map(port)
    except RevitMcpError as ex:
        print(
            json.dumps(
                {"ok": False, "code": "MCP_ERROR", "msg": str(ex), "where": ex.where},
                ensure_ascii=False,
                indent=2,
            )
        )
        return 1

    rows: List[Dict[str, Any]] = []
    flagged: List[Dict[str, Any]] = []

    for aid, info in sorted(asset_props.items(), key=lambda kv: kv[0]):
        lam = info.get("lambda_W_per_mK")
        flags = simple_lambda_flags(lam) if lam is not None else []
        row = {
            "assetId": aid,
            "name": info.get("name"),
            "lambda_W_per_mK": lam,
            "density_kg_per_m3": info.get("density_kg_per_m3"),
            "specificHeat_J_per_kgK": info.get("specificHeat_J_per_kgK"),
            "materialIds": asset_to_mats.get(aid, []),
            "flags": flags,
        }
        rows.append(row)
        if flags:
            flagged.append(row)

    out_obj = {
        "ok": True,
        "totalAssets": len(rows),
        "flaggedCount": len(flagged),
        "assets": rows,
        "flagged": flagged,
    }

    # 標準出力
    print(json.dumps(out_obj, ensure_ascii=False, indent=2))

    # ファイル出力（Codex ルート基準）
    try:
        codex_root = Path(__file__).resolve().parents[2]
        out_path = codex_root / args.output_json
        out_path.parent.mkdir(parents=True, exist_ok=True)
        out_path.write_text(json.dumps(out_obj, ensure_ascii=False, indent=2), encoding="utf-8")
    except Exception:
        # 出力に失敗しても致命的ではないので無視
        pass

    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

