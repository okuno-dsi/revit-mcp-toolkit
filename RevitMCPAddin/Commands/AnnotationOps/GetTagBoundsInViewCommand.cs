// File: RevitMCPAddin/Commands/AnnotationOps/GetTagBoundsInViewCommand.cs
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.AnnotationOps
{
    /// <summary>
    /// Returns a tag's view-projected bounding box (mm) when available.
    /// Params: { viewId:int, tagId?:int, uniqueId?:string, inflateMm?:double }
    /// Result: { ok, tagId, uniqueId, widthMm, heightMm, center:{x,y,z}, bbox:{min:{},max:{}} }
    /// </summary>
    public class GetTagBoundsInViewCommand : IRevitCommandHandler
    {
        public string CommandName => "get_tag_bounds_in_view";

        private static double FtToMm(double ft) => UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = (JObject)(cmd.Params ?? new JObject());
            var view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("viewId"))) as View;
            if (view == null) return new { ok = false, msg = "View not found." };

            Element tag = null;
            if (p.TryGetValue("tagId", out var idTok)) tag = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(idTok.Value<int>()));
            else if (p.TryGetValue("uniqueId", out var uTok)) tag = doc.GetElement(uTok.Value<string>());
            if (tag == null) return new { ok = false, msg = "Tag not found (tagId/uniqueId)." };

            if (!(tag is IndependentTag it)) return new { ok = false, msg = "Element is not an IndependentTag." };

            var bb = tag.get_BoundingBox(view);
            if (bb == null)
            {
                // As a fallback, synthesize a small bbox around head
                var c = it.TagHeadPosition; // ft
                double d = UnitUtils.ConvertToInternalUnits(200, UnitTypeId.Millimeters); // 200mm box
                bb = new BoundingBoxXYZ { Min = new XYZ(c.X - d, c.Y - d, c.Z), Max = new XYZ(c.X + d, c.Y + d, c.Z) };
            }

            var min = bb.Min; var max = bb.Max;
            var widthMm = Math.Max(0.0, FtToMm(max.X - min.X));
            var heightMm = Math.Max(0.0, FtToMm(max.Y - min.Y));
            var cx = 0.5 * (min.X + max.X); var cy = 0.5 * (min.Y + max.Y); var cz = 0.5 * (min.Z + max.Z);

            double inflateMm = p.Value<double?>("inflateMm") ?? 0.0;
            if (inflateMm > 0)
            {
                widthMm += 2 * inflateMm;
                heightMm += 2 * inflateMm;
            }

            return new
            {
                ok = true,
                tagId = it.Id.IntValue(),
                uniqueId = it.UniqueId,
                widthMm = Math.Round(widthMm, 1),
                heightMm = Math.Round(heightMm, 1),
                center = new { x = Math.Round(FtToMm(cx), 1), y = Math.Round(FtToMm(cy), 1), z = Math.Round(FtToMm(cz), 1) },
                bbox = new
                {
                    min = new { x = Math.Round(FtToMm(min.X), 1), y = Math.Round(FtToMm(min.Y), 1), z = Math.Round(FtToMm(min.Z), 1) },
                    max = new { x = Math.Round(FtToMm(max.X), 1), y = Math.Round(FtToMm(max.Y), 1), z = Math.Round(FtToMm(max.Z), 1) }
                }
            };
        }
    }
}



