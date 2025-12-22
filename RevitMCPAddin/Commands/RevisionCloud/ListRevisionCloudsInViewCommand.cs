#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using static Autodesk.Revit.DB.UnitUtils;

namespace RevitMCPAddin.Commands.RevisionCloud
{
    /// <summary>
    /// list_revision_clouds_in_view: enumerate revision cloud elements in a specific view.
    /// Params: { viewId:int }
    /// Returns: { ok:true, count:int, clouds:[ { elementId:int, bboxMm:{...}, comments?:string } ] }
    /// </summary>
    public class ListRevisionCloudsInViewCommand : IRevitCommandHandler
    {
        public string CommandName => "list_revision_clouds_in_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, code = "NO_DOC", msg = "No active document." };

            var p = (JObject)(cmd.Params ?? new JObject());
            int vid = p.Value<int?>("viewId") ?? 0;
            if (vid <= 0) return new { ok = false, code = "NO_VIEW", msg = "viewId is required." };

            var view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(vid)) as View;
            if (view == null) return new { ok = false, code = "NO_VIEW", msg = $"View not found: {vid}" };

            var res = new List<object>();
            try
            {
                var clouds = new FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.RevisionCloud))
                    .Cast<Autodesk.Revit.DB.RevisionCloud>()
                    .Where(c => c.OwnerViewId == view.Id)
                    .ToList();

                foreach (var c in clouds)
                {
                    var bb = c.get_BoundingBox(null);
                    object bbmm = null;
                    if (bb != null)
                    {
                        bbmm = new
                        {
                            min = new { x = Math.Round(ConvertFromInternalUnits(bb.Min.X, UnitTypeId.Millimeters), 3),
                                        y = Math.Round(ConvertFromInternalUnits(bb.Min.Y, UnitTypeId.Millimeters), 3),
                                        z = Math.Round(ConvertFromInternalUnits(bb.Min.Z, UnitTypeId.Millimeters), 3) },
                            max = new { x = Math.Round(ConvertFromInternalUnits(bb.Max.X, UnitTypeId.Millimeters), 3),
                                        y = Math.Round(ConvertFromInternalUnits(bb.Max.Y, UnitTypeId.Millimeters), 3),
                                        z = Math.Round(ConvertFromInternalUnits(bb.Max.Z, UnitTypeId.Millimeters), 3) }
                        };
                    }

                    string comments = null;
                    try
                    {
                        var p1 = c.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                        if (p1 != null) comments = p1.AsString();
                    }
                    catch { }

                    res.Add(new { elementId = c.Id.IntValue(), bboxMm = bbmm, comments });
                }

                return new { ok = true, count = res.Count, clouds = res };
            }
            catch (Exception ex)
            {
                return new { ok = false, code = "EXCEPTION", msg = ex.Message };
            }
        }
    }
}



