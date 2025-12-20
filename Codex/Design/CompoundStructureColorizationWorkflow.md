# Compound Structure Layer Colorization Workflow (RevitMCP)
### Design Document (English Markdown Version)

## 1. Purpose
This workflow provides a simple and effective way to visualize the internal composition of Revit compound structures such as walls and floors. Each layer is colorized according to its MaterialFunctionAssignment, allowing quick QA before IFC export or design review.

Outputs can be exported as DWG for CAD review or PNG images for documentation.

## 2. Overview of Functionality
The process automatically:
1. Reads the compound structure of selected walls/floors.
2. Maps each layer’s function to a predefined color.
3. Applies temporary OverrideGraphicSettings in a view.
4. Exports the visual result as DWG or PNG.
5. Optionally generates a section view for layer inspection.

## 3. RevitMCP Command Specification
### JSON-RPC Command: colorize_compound_layers
```json
{
  "name": "colorize_compound_layers",
  "params": {
    "category": "Walls|Floors",
    "view_type": "Section|Plan|3D",
    "function_color_map": {
      "Structure": "#C00000",
      "Substrate": "#7030A0",
      "Thermal": "#00B050",
      "Finish1": "#0070C0",
      "Finish2": "#FFC000",
      "Membrane": "#666666",
      "Other": "#999999"
    },
    "lineweight": 4,
    "export": {
      "dwg": { "enabled": true, "out_dir": "C:\\Out\\LayerQA\\DWG" },
      "image": { "enabled": true, "out_dir": "C:\\Out\\LayerQA\\IMG", "dpi": 300 }
    },
    "selection": "active_view|all_in_model|ids:[1234,5678]"
  }
}
```

## 4. Core Implementation (C# Sketch)
```csharp
public object Execute(UIApplication uiapp, RequestCommand cmd)
{
    var doc = uiapp.ActiveUIDocument.Document;
    var opt = cmd.Params.ToObject<ColorizeOptions>();

    IEnumerable<Element> targets = CollectElements(doc, opt.Category);

    foreach (var elem in targets)
    {
        var cs = GetCompoundStructure(elem);
        if (cs == null) continue;

        for (int i = 0; i < cs.LayerCount; i++)
        {
            var func = cs.GetLayerFunction(i);
            var color = ResolveColor(func, opt.FunctionColorMap);

            ApplyLayerOverrideInView(doc, opt.ActiveView, elem, i, color, opt.LineWeight);
        }
    }

    if (opt.Export.Dwg.Enabled)
        ExportDWG(doc, opt.ActiveView, opt.Export.Dwg.OutDir);

    if (opt.Export.Image.Enabled)
        ExportImage(doc, opt.ActiveView, opt.Export.Image.OutDir, opt.Export.Image.Dpi);

    return new { status = "ok" };
}
```

## 5. Recommended Color Palette
| Function | Color |
|---------|--------|
| Structure | #C00000 |
| Substrate | #7030A0 |
| Thermal | #00B050 |
| Finish1 | #0070C0 |
| Finish2 | #FFC000 |
| Membrane | #666666 |
| Other | #999999 |

## 6. Usage Workflow
1. Automatically generate a section cutting through elements.
2. Run colorize_compound_layers to apply layer colors.
3. Export as DWG or PNG.

## 7. QA Benefits
- Detect incorrect layer order  
- Identify missing or misclassified layers  
- Spot inconsistent types across the model  

## 8. Extensions
- Legend view generation  
- CSV/XLSX export of layer thickness summary  
- IFC pre-export validation  
- Before/after diff visualization  

## 9. File Output Examples
### DWG
- Preserves linework and color logic  

### PNG
- High-resolution documentation image  

## 10. Conclusion
This workflow enables fast and visually intuitive QA for compound structures in Revit, allowing early error detection and improved documentation quality.
