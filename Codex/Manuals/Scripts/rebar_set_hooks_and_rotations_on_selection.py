import argparse
import json
import os
import sys
import time
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple


def _add_scripts_to_path() -> None:
    here = Path(__file__).resolve().parent
    if str(here) not in sys.path:
        sys.path.insert(0, str(here))


_add_scripts_to_path()

from send_revit_command_durable import RevitMcpError, send_request  # type: ignore  # noqa: E402


def unwrap(envelope: Dict[str, Any]) -> Dict[str, Any]:
    obj: Any = envelope
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict):
        return obj
    return {}


def _default_port() -> int:
    v = os.environ.get("REVIT_MCP_PORT", "").strip()
    if not v:
        return 5210
    try:
        return int(v)
    except Exception:
        return 5210


def _repo_root() -> Path:
    return Path(__file__).resolve().parents[2]


def _out_dir(port: int) -> Path:
    return _repo_root() / "Work" / "RevitMcp" / str(port)


def _timestamp() -> str:
    return time.strftime("%Y%m%d_%H%M%S")


def get_selected_ids(port: int) -> Tuple[List[int], Dict[str, Any]]:
    env = send_request(port, "get_selected_element_ids", {})
    res = unwrap(env)
    ids = [int(x) for x in (res.get("elementIds") or []) if int(x) > 0]
    return ids, res


def get_hook_params(port: int, element_id: int) -> Dict[str, Any]:
    env = send_request(
        port,
        "get_instance_parameters_bulk",
        {
            "elementIds": [int(element_id)],
            "paramKeys": [
                "始端のフック",
                "終端のフック",
                # Angle param names may differ by environment/version.
                "始端でのフックの回転",
                "終端でのフックの回転",
                "始端のフックの回転",
                "終端のフックの回転",
            ],
            "page": {"startIndex": 0, "batchSize": 50},
            "failureHandling": {"enabled": True, "mode": "rollback"},
        },
    )
    res = unwrap(env)
    if not res.get("ok"):
        raise RevitMcpError("get_instance_parameters_bulk", res.get("msg", "Failed to read hook params"))
    items = res.get("items") or []
    if not items or not items[0].get("ok"):
        raise RevitMcpError("get_instance_parameters_bulk", f"Failed to read elementId={element_id}")
    return items[0]


def normalize_180_deg(deg: float) -> float:
    if abs(deg - 180.0) < 1e-9:
        return 179.999
    if abs(deg + 180.0) < 1e-9:
        return -179.999
    return deg


def set_param_for_elements(port: int, element_ids: List[int], param_name: str, value: Dict[str, Any]) -> Dict[str, Any]:
    env = send_request(
        port,
        "set_parameter_for_elements",
        {
            "elementIds": element_ids,
            "param": {"name": param_name},
            "value": value,
            "options": {"stopOnFirstError": False, "skipReadOnly": True, "ignoreMissingOnElement": True},
            "failureHandling": {"enabled": True, "mode": "rollback"},
        },
    )
    return unwrap(env)


def verify(port: int, element_ids: List[int]) -> Dict[str, Any]:
    env = send_request(
        port,
        "get_instance_parameters_bulk",
        {
            "elementIds": element_ids,
            "paramKeys": [
                "始端のフック",
                "終端のフック",
                "始端でのフックの回転",
                "終端でのフックの回転",
                "始端のフックの回転",
                "終端のフックの回転",
            ],
            "page": {"startIndex": 0, "batchSize": 500},
            "failureHandling": {"enabled": True, "mode": "rollback"},
        },
    )
    return unwrap(env)


def main(argv: List[str]) -> int:
    ap = argparse.ArgumentParser(
        description=(
            "選択中の鉄筋(Rebar)に対して、始端/終端フックと回転を一括設定します。"
            "デフォルトは「選択の先頭を参照鉄筋」にし、そのフック設定を残りにコピーします。"
        )
    )
    ap.add_argument("--port", type=int, default=_default_port(), help="Revit MCP ポート番号 (env: REVIT_MCP_PORT)")
    ap.add_argument(
        "--reference-id",
        type=int,
        default=0,
        help="参照鉄筋elementId（省略時は選択先頭を参照として扱う）。",
    )
    ap.add_argument(
        "--hook-type-id",
        type=int,
        default=0,
        help="始端/終端フックに設定する RebarHookType の elementId（参照コピーを使わない場合に指定）。",
    )
    ap.add_argument("--start-rot-deg", type=float, default=0.0, help="始端フック回転(度)。既定0。")
    ap.add_argument("--end-rot-deg", type=float, default=180.0, help="終端フック回転(度)。既定180。")
    ap.add_argument(
        "--no-180-workaround",
        action="store_true",
        help="180度を179.999度に置換する回避策を無効化します（Revitが180→0に正規化する場合があります）。",
    )
    ap.add_argument(
        "--dry-run",
        action="store_true",
        help="変更は行わず、参照/適用予定の値を表示します。",
    )
    args = ap.parse_args(argv)

    out_dir = _out_dir(args.port)
    out_dir.mkdir(parents=True, exist_ok=True)
    ts = _timestamp()

    try:
        selected_ids, sel_ctx = get_selected_ids(args.port)
    except RevitMcpError as ex:
        print(json.dumps({"ok": False, "code": "MCP_ERROR", "msg": str(ex)}, ensure_ascii=False, indent=2))
        return 2

    if not selected_ids:
        print(
            json.dumps(
                {
                    "ok": False,
                    "code": "NO_SELECTION",
                    "msg": "選択要素がありません。鉄筋(Rebar)を選択してください。",
                    "activeViewId": sel_ctx.get("activeViewId"),
                },
                ensure_ascii=False,
                indent=2,
            )
        )
        return 1

    reference_id = int(args.reference_id or 0)
    target_ids = selected_ids[:]
    if reference_id <= 0:
        reference_id = target_ids[0]
        target_ids = target_ids[1:]
    else:
        target_ids = [x for x in target_ids if x != reference_id]

    if not target_ids and args.hook_type_id <= 0:
        print(
            json.dumps(
                {
                    "ok": False,
                    "code": "NO_TARGETS",
                    "msg": "適用対象がありません。参照+対象（2つ以上）を選択するか、--hook-type-id を指定してください。",
                    "selectedIds": selected_ids,
                    "referenceId": reference_id,
                },
                ensure_ascii=False,
                indent=2,
            )
        )
        return 1

    plan: Dict[str, Any] = {"referenceId": reference_id, "targets": target_ids}
    if args.hook_type_id > 0:
        plan["mode"] = "set"
        plan["startHookId"] = int(args.hook_type_id)
        plan["endHookId"] = int(args.hook_type_id)
        start_rot = float(args.start_rot_deg)
        end_rot = float(args.end_rot_deg)
        if not args.no_180_workaround:
            start_rot = normalize_180_deg(start_rot)
            end_rot = normalize_180_deg(end_rot)
        plan["startRotationDeg"] = start_rot
        plan["endRotationDeg"] = end_rot
    else:
        plan["mode"] = "copy_from_reference"

    # Read reference (always, for verification/log)
    try:
        ref = get_hook_params(args.port, reference_id)
    except RevitMcpError as ex:
        print(json.dumps({"ok": False, "code": "MCP_ERROR", "msg": str(ex)}, ensure_ascii=False, indent=2))
        return 2

    plan["referenceHookParams"] = {
        "params": ref.get("params"),
        "display": ref.get("display"),
    }

    if plan["mode"] == "copy_from_reference":
        p = ref.get("params") or {}
        start_hook_id = int(p.get("始端のフック") or 0)
        end_hook_id = int(p.get("終端のフック") or 0)
        start_rot = float(p.get("始端でのフックの回転") or p.get("始端のフックの回転") or 0.0)
        end_rot = float(p.get("終端でのフックの回転") or p.get("終端のフックの回転") or 0.0)
        if not args.no_180_workaround:
            start_rot = normalize_180_deg(start_rot)
            end_rot = normalize_180_deg(end_rot)
        plan["startHookId"] = start_hook_id
        plan["endHookId"] = end_hook_id
        plan["startRotationDeg"] = start_rot
        plan["endRotationDeg"] = end_rot

    if args.dry_run:
        out = {"ok": True, "dryRun": True, "plan": plan, "selectedIds": selected_ids}
        print(json.dumps(out, ensure_ascii=False, indent=2))
        (out_dir / f"rebar_hooks_dryrun_{ts}.json").write_text(json.dumps(out, ensure_ascii=False, indent=2), encoding="utf-8")
        return 0

    if not target_ids:
        # When using --hook-type-id we allow a single selection target; in copy mode we require a target list.
        target_ids = selected_ids
        plan["targets"] = target_ids

    # Apply
    start_hook_id = int(plan.get("startHookId") or 0)
    end_hook_id = int(plan.get("endHookId") or 0)
    start_rot = float(plan.get("startRotationDeg") or 0.0)
    end_rot = float(plan.get("endRotationDeg") or 0.0)

    results: Dict[str, Any] = {"ok": True, "plan": plan, "updates": []}
    try:
        r1 = set_param_for_elements(
            args.port,
            target_ids,
            "始端のフック",
            {"storageType": "ElementId", "elementIdValue": start_hook_id},
        )
        results["updates"].append({"param": "始端のフック", "result": r1})
        r2 = set_param_for_elements(
            args.port,
            target_ids,
            "終端のフック",
            {"storageType": "ElementId", "elementIdValue": end_hook_id},
        )
        results["updates"].append({"param": "終端のフック", "result": r2})
        # Rotation param names can differ; attempt both and rely on ignoreMissingOnElement=true.
        r3a = set_param_for_elements(
            args.port,
            target_ids,
            "始端でのフックの回転",
            {"storageType": "Double", "doubleValue": start_rot},
        )
        results["updates"].append({"param": "始端でのフックの回転", "result": r3a})
        r3b = set_param_for_elements(
            args.port,
            target_ids,
            "始端のフックの回転",
            {"storageType": "Double", "doubleValue": start_rot},
        )
        results["updates"].append({"param": "始端のフックの回転", "result": r3b})
        r4a = set_param_for_elements(
            args.port,
            target_ids,
            "終端でのフックの回転",
            {"storageType": "Double", "doubleValue": end_rot},
        )
        results["updates"].append({"param": "終端でのフックの回転", "result": r4a})
        r4b = set_param_for_elements(
            args.port,
            target_ids,
            "終端のフックの回転",
            {"storageType": "Double", "doubleValue": end_rot},
        )
        results["updates"].append({"param": "終端のフックの回転", "result": r4b})
    except RevitMcpError as ex:
        results = {"ok": False, "code": "MCP_ERROR", "msg": str(ex), "plan": plan, "selectedIds": selected_ids}
        print(json.dumps(results, ensure_ascii=False, indent=2))
        (out_dir / f"rebar_hooks_error_{ts}.json").write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")
        return 2

    # Verify
    v = verify(args.port, [reference_id] + target_ids)
    results["verify"] = v

    out_path = out_dir / f"rebar_hooks_apply_{ts}.json"
    out_path.write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"ok": True, "savedTo": str(out_path)}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
