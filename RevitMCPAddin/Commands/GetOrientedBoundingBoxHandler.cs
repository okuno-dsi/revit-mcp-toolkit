// ================================================================
// File: Commands/GetOrientedBoundingBoxHandler.cs
// Purpose : JSON-RPC "get_oriented_bbox" の実体（返却を mm へ統一）
// Depends : RevitMCPAddin.Core (IRevitCommandHandler, RequestCommand, UnitHelper)
//           Core/Geometry/OrientedBoundingBox.cs
//           Core/Geometry/OrientedBoundingBoxUtil.cs
// Target  : .NET Framework 4.8 / Revit 2023+ / C# 8
// Notes   : すべての単位変換は UnitHelper に委譲（Length=mm, Volume=mm3）
// ================================================================
#nullable enable
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.Core.Geometry;

namespace RevitMCPAddin.Commands
{
    public class GetOrientedBoundingBoxHandler : IRevitCommandHandler
    {
        public string CommandName => "get_oriented_bbox";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var uidoc = uiapp.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc == null)
                    return new { ok = false, msg = "アクティブドキュメントがありません。" };

                var p = (JObject)(cmd.Params ?? new JObject());

                // 必須: elementId
                if (!p.TryGetValue("elementId", out var eidTok))
                    return new { ok = false, msg = "elementId が必要です。" };

                var elementId = Autodesk.Revit.DB.ElementIdCompat.From(eidTok.Value<int>());

                // 任意
                string strategy = (p.Value<string>("strategy") ?? "auto").Trim();
                string detailLevel = (p.Value<string>("detailLevel") ?? "fine").Trim().ToLower();
                bool includeCorners = p.Value<bool?>("includeCorners") ?? true;

                // 計算（内部 ft / rad）
                var resp = OrientedBoundingBoxUtil.TryCompute(doc, elementId, strategy, detailLevel, includeCorners);
                if (!resp.Ok)
                {
                    return new
                    {
                        ok = false,
                        msg = resp.Msg,
                        // 参考情報（既定 SI メタ）
                        units = new
                        {
                            expected = UnitHelper.DefaultUnitsMeta(),
                            internalUnits = new { Length = "ft", Angle = "rad" }
                        }
                    };
                }

                var o = resp.Obb;

                // ---------- ここで mm へ変換 ----------
                // center
                var cmm = UnitHelper.XyzToMm(o.Center);
                var centerMm = new { x = Math.Round(cmm.x, 3), y = Math.Round(cmm.y, 3), z = Math.Round(cmm.z, 3) };

                // unit axes（無次元）
                var axisX = new { x = o.AxisX.X, y = o.AxisX.Y, z = o.AxisX.Z };
                var axisY = new { x = o.AxisY.X, y = o.AxisY.Y, z = o.AxisY.Z };
                var axisZ = new { x = o.AxisZ.X, y = o.AxisZ.Y, z = o.AxisZ.Z };

                // extents（half sizes, ft→mm）
                var extentMm = new
                {
                    x = Math.Round(UnitHelper.FtToMm(o.ExtentX), 3),
                    y = Math.Round(UnitHelper.FtToMm(o.ExtentY), 3),
                    z = Math.Round(UnitHelper.FtToMm(o.ExtentZ), 3)
                };

                // corners（ft→mm）
                object[] cornersMm = null;
                if (includeCorners && o.Corners != null && o.Corners.Count > 0)
                {
                    cornersMm = o.Corners
                        .Select(c =>
                        {
                            var t = UnitHelper.XyzToMm(c);
                            return (object)new { x = Math.Round(t.x, 3), y = Math.Round(t.y, 3), z = Math.Round(t.z, 3) };
                        })
                        .ToArray();
                }

                // 体積: ft^3 → m^3 → mm^3
                // UnitHelper は m^3 まで提供しているため、× 1e9 で mm^3 へ
                var volumeMm3 = Math.Round(UnitHelper.InternalToCubicMeters(o.Volume) * 1_000_000_000.0, 3);

                return new
                {
                    ok = true,
                    obb = new
                    {
                        center = centerMm,
                        axisX,
                        axisY,
                        axisZ,
                        extentMm,     // half sizes in mm
                        corners = cornersMm, // optional
                        volumeMm3,    // mm^3
                        notes = o.Notes
                    },
                    units = new
                    {
                        coordinates = "mm",
                        extents = "mm",
                        volume = "mm3",
                        axes = "unit vector (world)"
                    }
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "Exception: " + ex.Message };
            }
        }
    }
}

