// ================================================================
// File   : Commands/ViewOps/ViewPlacementCommands.cs
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// Summary: Returns whether a view is placed on any sheet,
//          and details for each placement (sheet info + viewport box in mm).
// I/O    : Input  -> { viewId:int }
//          Output -> { ok, viewId, placed, placements:[{ viewportId, sheetId, sheetNumber, sheetName, viewportBoxMm? }] }
// Notes  : Read-only; no transactions required.
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using static Autodesk.Revit.DB.UnitUtils;

namespace RevitMCPAddin.Commands.ViewOps
{
    public class GetViewPlacementsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_view_placements";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null)
                    return new { ok = false, msg = "アクティブドキュメントが見つかりません。" };

                var p = (JObject)cmd.Params;
                int viewId = p.Value<int?>("viewId") ?? 0;
                string viewUid = p.Value<string>("viewUniqueId");

                View view = null;
                if (viewId > 0) view = doc.GetElement(new ElementId(viewId)) as View;
                if (view == null && !string.IsNullOrWhiteSpace(viewUid)) view = doc.GetElement(viewUid) as View;
                if (view == null)
                    return new { ok = false, msg = $"ビューが見つかりません: viewId={viewId}" };

                var placements = new List<object>();

                // Collect all viewports referencing this view
                IEnumerable<Viewport> vps = Enumerable.Empty<Viewport>();
                try
                {
                    vps = new FilteredElementCollector(doc)
                        .OfClass(typeof(Viewport))
                        .Cast<Viewport>()
                        .Where(vp => vp.ViewId.IntegerValue == view.Id.IntegerValue)
                        .ToList();
                }
                catch { /* ignore */ }

                foreach (var vp in vps)
                {
                    var sheet = doc.GetElement(vp.SheetId) as ViewSheet;
                    int viewportId = vp.Id.IntegerValue;
                    int sheetId = sheet?.Id.IntegerValue ?? 0;
                    string sheetNumber = sheet?.SheetNumber ?? string.Empty;
                    string sheetName = sheet?.Name ?? string.Empty;

                    object box = null;
                    try
                    {
                        var outline = vp.GetBoxOutline();
                        if (outline != null)
                        {
                            var min = outline.MinimumPoint; // internal (ft)
                            var max = outline.MaximumPoint; // internal (ft)
                            box = new
                            {
                                minX = ConvertFromInternalUnits(min.X, UnitTypeId.Millimeters),
                                minY = ConvertFromInternalUnits(min.Y, UnitTypeId.Millimeters),
                                maxX = ConvertFromInternalUnits(max.X, UnitTypeId.Millimeters),
                                maxY = ConvertFromInternalUnits(max.Y, UnitTypeId.Millimeters)
                            };
                        }
                    }
                    catch { /* optional */ }

                    placements.Add(new
                    {
                        viewportId,
                        sheetId,
                        sheetNumber,
                        sheetName,
                        viewportBoxMm = box
                    });
                }

                return new
                {
                    ok = true,
                    viewId = view.Id.IntegerValue,
                    placed = placements.Count > 0,
                    placements
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }
}
