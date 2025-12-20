# Hot Commands Cheatsheet

Common operations (updated):
- get_elements_in_view (read)
- save_view_state (read) / restore_view_state (write)
- set_visual_override (write)
- get_element_info (read)
- create_wall (write)
- move_element (write)
- update_element (write)
- export_dwg (write)

Examples (port 5210):
- Ping: `python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command ping_server`
- Bootstrap: `python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command agent_bootstrap`
- Create Room: `python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command create_room --params '{"levelName":"1FL","x":1500,"y":1500,"__smoke_ok":true}'`  (defaults: autoTag=true, strictEnclosure=true)
- List visible element IDs (replace <viewId>):
  `python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command get_elements_in_view --params "{\"viewId\": <viewId>, \"_shape\":{\"idsOnly\":true,\"page\":{\"limit\":200}}}"`
- Visual override (red, 60%):
  `python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command set_visual_override --params "{\"elementId\": <id>, \"color\":{\"r\":255,\"g\":0,\"b\":0}, \"transparency\":60, \"__smoke_ok\":true}"`

- Duplicate view (idempotent, safe name):
  `python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command duplicate_view --params '{"viewId": <viewId>, "desiredName":"<Base> Copy", "onNameConflict":"returnExisting", "idempotencyKey":"dup:<baseUid>:<Base> Copy"}'`

Isolate by Parameter (Structural Framing)
- Keep only Structural Framing whose TYPE parameter "符号" contains "B":
  `python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command isolate_by_filter_in_view --params '{"viewId": <viewId>, "detachViewTemplate": true, "reset": true, "keepAnnotations": true, "filter": {"includeCategoryNames":["StructuralFraming"], "parameterRuleGroups":[[{"target":"type","name":"符号","op":"contains","value":"B","caseInsensitive":true}]]}}'`
- Same for "G": replace value with "G".
- Seed (others): hide elements matching "B" OR "G" by using invertMatch:
  `python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command isolate_by_filter_in_view --params '{"viewId": <viewId>, "detachViewTemplate": true, "reset": true, "keepAnnotations": true, "filter": {"includeCategoryNames":["StructuralFraming"], "parameterRuleGroups":[[{"target":"type","name":"符号","op":"contains","value":"B","caseInsensitive":true}],[{"target":"type","name":"符号","op":"contains","value":"G","caseInsensitive":true}]], "invertMatch": true}}'`

- Rename view (replace <viewId>):
  `python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command rename_view --params '{"viewId": <viewId>, "newName": "My_View"}'`

- Save/Restore view state (replace <viewId>):
  - Save: `python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command save_view_state --params "{}" --output-file Work/<ProjectName>_<Port>/Logs/view_state.json`
  - Restore: prepare a payload `{ "viewId": <viewId>, "state": { ...from view_state.json... }, "apply": { "template": true, "categories": true, "filters": true, "worksets": true } }` and run:
    `python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command restore_view_state --params-file Work/<ProjectName>_<Port>/Logs/view_state_payload.json`

Caution
- Deprecated here: reset_all_view_overrides / reset_category_override / set_category_visibility / unhide_elements_in_view (use save/restore flow instead).
- Do not send `viewId: 0` or `elementId: 0`.
- In saved JSON outputs, target values are at `result.result.*` within the JSON-RPC envelope.


