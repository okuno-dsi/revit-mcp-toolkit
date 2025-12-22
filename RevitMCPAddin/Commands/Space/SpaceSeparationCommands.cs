#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;     // UnitHelper, ResultUtil, IRevitCommandHandler
using RevitMCPAddin.Models;   // Point3D

namespace RevitMCPAddin.Commands.SpaceOps.Separation
{
    internal static class SpaceSepUtil
    {
        public static SketchPlane GetOrCreateSketchPlaneForView(Document doc, View view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (view.SketchPlane != null) return view.SketchPlane;
            var plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, 0));
            using (var tx = new Transaction(doc, "Create SketchPlane for SpaceSep"))
            {
                tx.Start();
                var sp = SketchPlane.Create(doc, plane);
                tx.Commit();
                return sp;
            }
        }

        public static Curve ToInternalCurveFromMm(JObject seg)
        {
            var kind = seg.Value<string>("kind") ?? seg.Value<string>("type");
            if (string.Equals(kind, "line", StringComparison.OrdinalIgnoreCase))
            {
                var s = seg["start"]!.ToObject<Point3D>()!;
                var e = seg["end"]!.ToObject<Point3D>()!;
                var p1 = UnitHelper.MmToXyz(s.X, s.Y, s.Z);
                var p2 = UnitHelper.MmToXyz(e.X, e.Y, e.Z);
                return Line.CreateBound(p1, p2);
            }
            if (string.Equals(kind, "arc", StringComparison.OrdinalIgnoreCase))
            {
                var c = seg["center"]!.ToObject<Point3D>()!;
                var center = UnitHelper.MmToXyz(c.X, c.Y, c.Z);
                var r = UnitHelper.MmToInternalLength(seg.Value<double>("radiusMm"));
                var a0 = UnitHelper.DegToRad(seg.Value<double>("startAngleDeg"));
                var a1 = UnitHelper.DegToRad(seg.Value<double>("endAngleDeg"));
                var p0 = center + new XYZ(r * Math.Cos(a0), r * Math.Sin(a0), 0);
                var p1 = center + new XYZ(r * Math.Cos(a1), r * Math.Sin(a1), 0);
                var mid = center + new XYZ(r * Math.Cos((a0 + a1) / 2.0), r * Math.Sin((a0 + a1) / 2.0), 0);
                return Arc.Create(p0, p1, mid);
            }
            throw new InvalidOperationException($"Unsupported curve segment kind='{kind}'.");
        }

        public static IEnumerable<Curve> BuildCurveArrayFromPolylineJArray(JArray segments)
        {
            foreach (var s in segments.OfType<JObject>())
                yield return ToInternalCurveFromMm(s);
        }

        public static bool IsSpaceSeparationLine(Element e)
        {
            if (e is CurveElement ce)
                return ce.CurveElementType == CurveElementType.SpaceSeparation;
            return false;
        }

        private static bool IsNearlyPlanarXY(XYZ normal)
        {
            // Z±1方向に近ければXY平面とみなす（閾値はゆるめ）
            return Math.Abs(Math.Abs(normal.Z) - 1.0) < 1e-3;
        }
    }

    // ===== get_space_separation_lines_in_view =====
    public class GetSpaceSeparationLinesInViewCommand : IRevitCommandHandler
    {
        public string CommandName => "get_space_separation_lines_in_view";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject)cmd.Params;
            var viewId = p.Value<int?>("viewId");
            var view = viewId.HasValue ? doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId.Value)) as View : uiapp.ActiveUIDocument?.ActiveView;
            if (view == null) return ResultUtil.Err("対象ビューが見つかりません。");

            var els = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .Where(ce => ce.CurveElementType == CurveElementType.SpaceSeparation)
                .ToList();

            var items = new JArray();
            foreach (var ce in els)
            {
                var curve = ce.GeometryCurve;
                var o = new JObject { ["elementId"] = ce.Id.IntValue() };
                switch (curve)
                {
                    case Line ln:
                        {
                            var s = UnitHelper.XyzToMm(ln.GetEndPoint(0));
                            var e = UnitHelper.XyzToMm(ln.GetEndPoint(1));
                            o["kind"] = "line";
                            o["start"] = JToken.FromObject(s);
                            o["end"] = JToken.FromObject(e);
                            break;
                        }
                    case Arc ac:
                        {
                            o["kind"] = "arc";
                            // center / radius(mm)
                            var center = ac.Center;
                            var cMm = UnitHelper.XyzToMm(center);
                            o["center"] = JToken.FromObject(cMm);
                            o["radiusMm"] = UnitUtils.ConvertFromInternalUnits(ac.Radius, UnitTypeId.Millimeters);

                            // 角度（可能な場合のみ：法線が±Zに近い＝XY平面想定）
                            try
                            {
                                // Revit Arc には StartAngle/EndAngle が無いので、center→start/end ベクトルから算出
                                var n = (ac as Curve).ComputeDerivatives(0.5, true).BasisZ; // 中点の法線近似
                                bool planarXY = Math.Abs(Math.Abs(n.Z) - 1.0) < 1e-3;
                                if (!planarXY)
                                {
                                    // XYでなければ角度は省略（start/end点は返す）
                                    var sp = UnitHelper.XyzToMm(ac.GetEndPoint(0));
                                    var ep = UnitHelper.XyzToMm(ac.GetEndPoint(1));
                                    o["start"] = JToken.FromObject(sp);
                                    o["end"] = JToken.FromObject(ep);
                                    break;
                                }

                                var p0 = ac.GetEndPoint(0);
                                var p1 = ac.GetEndPoint(1);
                                var v0 = p0 - center;
                                var v1 = p1 - center;
                                var a0 = Math.Atan2(v0.Y, v0.X);
                                var a1 = Math.Atan2(v1.Y, v1.X);
                                o["startAngleDeg"] = UnitHelper.RadToDeg(a0);
                                o["endAngleDeg"] = UnitHelper.RadToDeg(a1);

                                // 併せて start/end 座標も返しておく
                                var spMm = UnitHelper.XyzToMm(p0);
                                var epMm = UnitHelper.XyzToMm(p1);
                                o["start"] = JToken.FromObject(spMm);
                                o["end"] = JToken.FromObject(epMm);
                            }
                            catch
                            {
                                // 何らかの理由で角度計算が難しければ座標のみ返す
                                var sp = UnitHelper.XyzToMm(ac.GetEndPoint(0));
                                var ep = UnitHelper.XyzToMm(ac.GetEndPoint(1));
                                o["start"] = JToken.FromObject(sp);
                                o["end"] = JToken.FromObject(ep);
                            }
                            break;
                        }
                    default:
                        o["kind"] = curve.GetType().Name;
                        break;
                }
                items.Add(o);
            }

            return ResultUtil.Ok(new
            {
                ok = true,
                viewId = view.Id.IntValue(),
                total = els.Count,
                lines = items,
                units = new { Length = "mm", Angle = "deg" }
            });
        }
    }

    // ===== create_space_separation_lines =====
    public class CreateSpaceSeparationLinesCommand : IRevitCommandHandler
    {
        public string CommandName => "create_space_separation_lines";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject)cmd.Params;
            var viewId = p.Value<int?>("viewId");
            var view = viewId.HasValue ? doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId.Value)) as View : uiapp.ActiveUIDocument?.ActiveView;
            if (view == null) return ResultUtil.Err("対象ビューが見つかりません。");

            var segments = p["segments"] as JArray;
            if (segments == null || segments.Count == 0) return ResultUtil.Err("segments が空です。");

            var sp = SpaceSepUtil.GetOrCreateSketchPlaneForView(doc, view);
            var curves = new CurveArray();
            foreach (var c in SpaceSepUtil.BuildCurveArrayFromPolylineJArray(segments))
                curves.Append(c);

            IList<ElementId> createdIds = new List<ElementId>();
            using (var tx = new Transaction(doc, "Create Space Separation Lines"))
            {
                tx.Start();
                var mca = doc.Create.NewSpaceBoundaryLines(sp, curves, view);
                foreach (ModelCurve mc in mca)
                    createdIds.Add(mc.Id);
                tx.Commit();
            }

            return ResultUtil.Ok(new
            {
                createdCount = createdIds.Count,
                elementIds = createdIds.Select(id => id.IntValue()).ToList(),
                units = new { Length = "mm", Angle = "deg" }
            });
        }
    }

    // ===== move_space_separation_line =====
    public class MoveSpaceSeparationLineCommand : IRevitCommandHandler
    {
        public string CommandName => "move_space_separation_line";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject)cmd.Params;
            var id = p.Value<int>("elementId");
            var dx = p.Value<double>("dx");
            var dy = p.Value<double>("dy");
            var dz = p.Value<double>("dz");
            var el = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id));
            if (el == null) return ResultUtil.Err($"elementId={id} が見つかりません。");
            if (!SpaceSepUtil.IsSpaceSeparationLine(el)) return ResultUtil.Err("指定要素はSpace Separation Lineではありません。");

            var t = UnitHelper.MmToXyz(dx, dy, dz);
            using (var tx = new Transaction(doc, "Move Space Separation Line"))
            {
                tx.Start();
                ElementTransformUtils.MoveElement(doc, el.Id, t);
                tx.Commit();
            }
            return ResultUtil.Ok(new { moved = 1, elementId = id });
        }
    }

    // ===== delete_space_separation_lines =====
    public class DeleteSpaceSeparationLinesCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_space_separation_lines";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject)cmd.Params;
            var ids = p["elementIds"]?.ToObject<List<int>>() ?? new List<int>();
            if (ids.Count == 0) return ResultUtil.Err("elementIds が空です。");

            var delTargets = new List<ElementId>();
            foreach (var i in ids)
            {
                var e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(i));
                if (e != null && SpaceSepUtil.IsSpaceSeparationLine(e))
                    delTargets.Add(e.Id);
            }
            if (delTargets.Count == 0) return ResultUtil.Ok(new { deleted = 0 });

            ICollection<ElementId> deleted;
            using (var tx = new Transaction(doc, "Delete Space Separation Lines"))
            {
                tx.Start();
                deleted = doc.Delete(delTargets);
                tx.Commit();
            }
            return ResultUtil.Ok(new { requested = ids.Count, deleted = deleted.Select(x => x.IntValue()).ToList() });
        }
    }
}


