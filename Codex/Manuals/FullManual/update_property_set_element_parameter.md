# update_property_set_element_parameter

- Category: ElementOps
- Purpose: Update a PropertySetElement (asset) parameter by built-in name or parameter name.

## Overview
This is a low-level helper intended mainly for material assets (e.g. thermal/physical property sets).

## Usage
- Method: `update_property_set_element_parameter`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | int | no / one of | 0 |
| uniqueId | string | no / one of |  |
| builtInName | string | no / one of |  |
| paramName | string | no / one of |  |
| value | any | yes |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_property_set_element_parameter",
  "params": {
    "elementId": 198872,
    "builtInName": "PHY_MATERIAL_PARAM_THERMAL_CONDUCTIVITY",
    "value": 0.5
  }
}
```

## Related
- get_material_asset_properties
- set_material_thermal_properties

