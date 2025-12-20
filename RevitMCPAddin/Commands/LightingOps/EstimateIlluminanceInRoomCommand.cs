// ================================================================
// Method : estimate_illuminance_in_room (rough)
// ================================================================
#nullable enable
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.LightingOps
{
    public sealed class EstimateIlluminanceInRoomCommand : IRevitCommandHandler
    {
        public string CommandName => "estimate_illuminance_in_room";

        public object Execute(UIApplication uiapp, RequestCommand req)
        {
            try
            {
                var doc = uiapp.ActiveUIDocument.Document;
                var p = ReqShim.Params(req);

                int id = ReqShim.Get<int?>(p, "id", null) ?? 0;
                if (id == 0) return new { ok = false, msg = "id required (Room/Space)" };

                int nx = ReqShim.GetPath<int?>(p, "grid.nx", null) ?? 5;
                int ny = ReqShim.GetPath<int?>(p, "grid.ny", null) ?? 5;
                double heightM = ReqShim.Get<double?>(p, "heightM", null) ?? 0.8;
                double lmPerW = ReqShim.Get<double?>(p, "lmPerW", null) ?? 80.0;

                var se = LightingCommon.GetSpatialElement(doc, new ElementId(id));
                if (se == null) return new { ok = false, msg = "Not a Room/Space" };

                var fixtures = LightingCommon.CollectFixtures(doc).Where(f =>
                {
                    var q = LightingCommon.GetLocationPoint(f);
                    return q != null && LightingCommon.IsInside(se, q);
                }).ToList();

                if (fixtures.Count == 0)
                    return new
                    {
                        ok = true,
                        id,
                        name = se.Name,
                        nx,
                        ny,
                        points = new double[0],
                        stats = new { min = 0.0, avg = 0.0, max = 0.0 }
                    };

                var bb = se.get_BoundingBox(null);
                if (bb == null) return new { ok = false, msg = "No bounding box" };

                var min = bb.Min; var max = bb.Max;

                // 観測面高さ(ft) = + heightM
                double z = min.Z + (heightM / 0.3048);

                var values = new List<double>(nx * ny);
                for (int iy = 0; iy < ny; iy++)
                {
                    double ty = (ny == 1) ? 0.5 : (iy + 0.5) / ny;
                    for (int ix = 0; ix < nx; ix++)
                    {
                        double tx = (nx == 1) ? 0.5 : (ix + 0.5) / nx;
                        var px = min.X + (max.X - min.X) * tx;
                        var py = min.Y + (max.Y - min.Y) * ty;
                        var pnt = new XYZ(px, py, z);

                        double accum = 0.0;
                        foreach (var f in fixtures)
                        {
                            var fp = LightingCommon.GetLocationPoint(f); if (fp == null) continue;
                            var r = pnt.DistanceTo(fp);
                            if (r < 0.1) r = 0.1;
                            var dir = (pnt - fp).Normalize();
                            double cos = System.Math.Abs(dir.Z); // 仮に下向き
                            double lm = LightingCommon.GetLumensOrEstimate(f, lmPerW);
                            double E = (lm / (4.0 * System.Math.PI * r * r)) * cos * 0.7; // lux 近似
                            accum += E;
                        }
                        values.Add(accum);
                    }
                }

                return new
                {
                    ok = true,
                    id,
                    name = se.Name,
                    nx,
                    ny,
                    points = values.ToArray(),
                    stats = new { min = values.Min(), avg = values.Average(), max = values.Max() },
                    note = "Rough estimate (isotropic + cosine; reflectance 0.7)."
                };
            }
            catch (System.Exception ex)
            {
                RevitLogger.LogError("[estimate_illuminance_in_room] " + ex);
                return new { ok = false, msg = ex.Message };
            }
        }
    }
}
