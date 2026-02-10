# Join Geometry Commands (Revit Join/Unjoin/Switch)

Manage Revit element joins and related constraints programmatically. Wraps `JoinGeometryUtils` and adds inspection helpers.

- Category: Other
- Kind: write/read (mixed)
- Affects model: yes (write ops start a transaction)

## Commands

1) `join_elements` – write  
- Input: `{ "elementIdA": int | "uniqueIdA": string, "elementIdB": int | "uniqueIdB": string }`  
- Behavior: Joins A and B (no-op if already joined; Revit rules apply)  
- Result: `{ ok, a, b }`

2) `unjoin_elements` – write  
- Input: same keys as above  
- Behavior: If A and B are joined, unjoin them  
- Result: `{ ok, a, b }`

3) `are_elements_joined` – read  
- Input: same keys as above  
- Result: `{ ok, joined: bool, a, b }`

4) `switch_join_order` – write  
- Input: same keys as above  
- Behavior: Ensures A and B are joined, then switches their join order  
- Result: `{ ok, a, b }`

5) `get_joined_elements` – read  
- Input: `{ "elementId": int | "uniqueId": string }`  
- Behavior: Inspects a single element for geometry joins and related constraint context.  
- Result (shape):  
  ```json
  {
    "ok": true,
    "elementId": 12345,
    "joinedIds": [11111, 22222],
    "hostId": 0,
    "superComponentId": null,
    "subComponentIds": [33333],
    "isPinned": true,
    "isInGroup": false,
    "groupId": null,
    "dependentIds": [44444, 55555],
    "suggestedCommands": [
      { "kind": "geometryJoin", "command": "unjoin_elements", "description": "Unjoin this element from its joined partners." },
      { "kind": "pin", "command": "unpin_element", "description": "Unpin this element so it can move." }
    ],
    "notes": [
      "Host / group / dimension-related operations should generally be performed in the Revit UI."
    ]
  }
  ```

6) `unpin_element` – write  
- Input: `{ "elementId": int | "uniqueId": string }`  
- Behavior: If the element is pinned, clears `Pinned` in a transaction. No-op when already unpinned.  
- Result: `{ ok, elementId, uniqueId, changed: bool, wasPinned: bool }`

7) `unpin_elements` – write  
- Input: `{ "elementIds": int[] }`  
- Behavior: Attempts to unpin each element in a single transaction.  
- Result: `{ ok, requested: int, processed: int, changed: int, failedIds: int[] }`

## Examples

Join two walls (by ids):
```
python -X utf8 Scripts/Reference/send_revit_command_durable.py \
  --port 5210 --command join_elements \
  --params '{"elementIdA":12345, "elementIdB":67890}' --force
```

Check joined:
```
python -X utf8 Scripts/Reference/send_revit_command_durable.py \
  --port 5210 --command are_elements_joined \
  --params '{"elementIdA":12345, "elementIdB":67890}' --force
```

Switch join order:
```
python -X utf8 Scripts/Reference/send_revit_command_durable.py \
  --port 5210 --command switch_join_order \
  --params '{"elementIdA":12345, "elementIdB":67890}' --force
```

Unjoin:
```
python -X utf8 Scripts/Reference/send_revit_command_durable.py \
  --port 5210 --command unjoin_elements \
  --params '{"elementIdA":12345, "elementIdB":67890}' --force
```

List joined/constraint info from one element:
```
python -X utf8 Scripts/Reference/send_revit_command_durable.py \
  --port 5210 --command get_joined_elements \
  --params '{"elementId":12345}' --force
```

Unpin a single element:
```
python -X utf8 Scripts/Reference/send_revit_command_durable.py \
  --port 5210 --command unpin_element \
  --params '{"elementId":12345}' --force
```

Unpin several elements at once:
```
python -X utf8 Scripts/Reference/send_revit_command_durable.py \
  --port 5210 --command unpin_elements \
  --params '{"elementIds":[12345,67890]}' --force
```

## Notes
- Not all categories can be joined; Revit’s own rules/errors apply.
- Switching order changes which element cuts the other.
- Prefer passing ElementIds. uniqueIds are supported for convenience.
- `get_joined_elements` also reports pin/group/host/dependent state and suggests safe follow-up commands (`unjoin_elements`, `unpin_element`) while warning about operations that should stay in Revit UI.



