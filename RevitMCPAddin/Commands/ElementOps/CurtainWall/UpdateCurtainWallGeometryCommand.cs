using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.CurtainWall
{
    public class UpdateCurtainWallGeometryCommand : IRevitCommandHandler
    {
        public string CommandName => "update_curtain_wall_geometry";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            int id = (int)p["elementId"]!;

            // mm → XYZ(ft)
            var pts = p["baseline"]!
                .Select(pt => UnitHelper.MmToXyz(
                                    (double)pt["x"],
                                    (double)pt["y"],
                                    (double)pt["z"]))
                .ToArray();

            double heightMm = (double)p["heightMm"]!;

            var doc = uiapp.ActiveUIDocument.Document;
            var wall = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id)) as Autodesk.Revit.DB.Wall
                       ?? throw new InvalidOperationException("Curtain wall not found");

            using (var tx = new Transaction(doc, "Update Curtain Wall Geometry"))
            {
                tx.Start();

                if (wall.Location is LocationCurve lc && pts.Length >= 2)
                    lc.Curve = Line.CreateBound(pts[0], pts[1]);

                var param = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                if (param != null && !param.IsReadOnly)
                    param.Set(UnitHelper.MmToFt(heightMm));

                tx.Commit();
            }

            return new { ok = true };
        }
    }
}

