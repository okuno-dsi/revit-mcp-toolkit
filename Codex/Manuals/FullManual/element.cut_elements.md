# element.cut_elements

- Category: Geometry / Cut
- Purpose: Cut one or more elements by a cutting element (Cut Geometry).

## Overview
Cuts the specified elements using a cutting element. The cutting element can be a structural foundation (and any other element that Revit allows as a cutter). The command skips items that cannot be cut when `skipIfCannotCut=true`.

## Usage
- Method: element.cut_elements

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| cuttingElementId | int | yes |  |
| cuttingUniqueId | string | no |  |
| cutElementIds | int[] | yes |  |
| cutElementUniqueIds | string[] | no |  |
| cutElementId | int | no |  |
| cutElementUniqueId | string | no |  |
| skipIfAlreadyCut | bool | no | true |
| skipIfCannotCut | bool | no | true |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "element.cut_elements",
  "params": {
    "cuttingElementId": 12345,
    "cutElementIds": [23456, 34567],
    "skipIfAlreadyCut": true,
    "skipIfCannotCut": true
  }
}
```

## Result
- `successIds`: cut element IDs successfully cut
- `skipped`: list of skipped elements and reasons
- `failed`: list of failures and messages

## Related
- element.uncut_elements
- element.join_elements
- element.unjoin_elements
