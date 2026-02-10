# element.uncut_elements

- Category: Geometry / Cut
- Purpose: Remove Cut Geometry between elements.

## Overview
Removes cut relationships between a cutting element and one or more cut elements.

## Usage
- Method: element.uncut_elements

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| cuttingElementId | int | yes |  |
| cuttingUniqueId | string | no |  |
| cutElementIds | int[] | yes |  |
| cutElementUniqueIds | string[] | no |  |
| cutElementId | int | no |  |
| cutElementUniqueId | string | no |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "element.uncut_elements",
  "params": {
    "cuttingElementId": 12345,
    "cutElementIds": [23456, 34567]
  }
}
```

## Result
- `successIds`: elements uncut successfully
- `failed`: failures and messages

## Related
- element.cut_elements
- element.join_elements
- element.unjoin_elements
