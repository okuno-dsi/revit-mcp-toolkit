// File: RevitMCPAddin/Commands/ElementOps/Mass/CreateMassByExtrusionCommand.cs
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Mass
{
    /// <summary>
    /// 2Dループ（mm座標）＋高さ(mm) から DirectShape の Mass を押し出しで作成
    /// </summary>
    public class CreateMassByExtrusionCommand : IRevitCommandHandler
    {
        public string CommandName => "create_direct_shape_mass";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            // 2D loops: array of loops, each loop is array of points {x,y}
            var loopsToken = p.Value<JArray>("loops")
                             ?? throw new ArgumentException("Parameter 'loops' is required.");

            // ← 修正ポイント：double で受けて .Value を使わない
            double heightMm = p.Value<double?>("height")
                              ?? throw new ArgumentException("Parameter 'height' is required.");

            bool useMassCat = p.Value<bool?>("useMassCategory") ?? true;
            var catId = useMassCat
                        ? Autodesk.Revit.DB.ElementIdCompat.From(BuiltInCategory.OST_Mass)
                        : Autodesk.Revit.DB.ElementIdCompat.From(BuiltInCategory.OST_GenericModel);

            // mm → ft
            double heightFt = UnitHelper.MmToFt(heightMm);

            // カーブループを構築（mm → ft）
            var boundaryLoops = new List<CurveLoop>();
            foreach (var loop in loopsToken)
            {
                var pts = loop as JArray;
                if (pts == null || pts.Count < 3) continue;

                var curves = new List<Curve>();
                var points = new List<XYZ>();
                foreach (var pt in pts)
                {
                    double xFt = UnitHelper.MmToFt(pt.Value<double>("x"));
                    double yFt = UnitHelper.MmToFt(pt.Value<double>("y"));
                    points.Add(new XYZ(xFt, yFt, 0));
                }

                for (int i = 0; i < points.Count; i++)
                {
                    var a = points[i];
                    var b = points[(i + 1) % points.Count];
                    curves.Add(Line.CreateBound(a, b));
                }
                var cl = CurveLoop.Create(curves);
                if (!cl.IsOpen() && cl.HasPlane())
                    boundaryLoops.Add(cl);
            }

            if (boundaryLoops.Count == 0)
                return new { ok = false, message = "No valid boundary loops provided." };

            // 押し出しソリッド作成
            GeometryObject solid;
            try
            {
                // 1つ目のループを押し出しに使用
                solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                    new List<CurveLoop> { boundaryLoops[0] }, XYZ.BasisZ, heightFt);
            }
            catch (Exception ex)
            {
                return new { ok = false, message = ex.Message };
            }

            // DirectShape 作成
            DirectShape ds;
            using (var tx = new Transaction(doc, "Create Direct Shape Mass"))
            {
                tx.Start();
                ds = DirectShape.CreateElement(doc, catId);
                ds.ApplicationId = "MCPMassBlock";
                ds.ApplicationDataId = Guid.NewGuid().ToString();
                ds.SetShape(new List<GeometryObject> { solid });
                tx.Commit();
            }

            return new { ok = true, elementId = ds.Id.IntValue() };
        }
    }
}


