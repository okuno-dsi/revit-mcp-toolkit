# create_legend_view_from_template

- Category: ViewOps
- Purpose: Duplicate an existing Legend view (template) to create a new Legend view.

## Overview
Revit API cannot create a Legend view from scratch; this command duplicates an existing Legend view.

## Usage
- Method: `create_legend_view_from_template`

### Parameters
```jsonc
{
  "baseLegendViewName": "DoorWindow_Legend_Template",   // optional
  "newLegendViewName": "DoorWindow_Legend_Elevations",  // optional
  "clearContents": false,                               // optional
  "applyViewTemplateName": null                         // optional (view template name)
}
```

