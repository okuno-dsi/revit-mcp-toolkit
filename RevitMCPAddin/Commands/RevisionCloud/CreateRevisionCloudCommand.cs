// File: RevitMCPAddin/Commands/RevisionCloud/CreateRevisionCloudCommand.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.RevisionCloud
{
    public class CreateRevisionCloudCommand : IRevitCommandHandler
    {
        public string CommandName => "create_revision_cloud";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp.ActiveUIDocument.Document;
                var p = (JObject)cmd.Params;

                // ——— 必須パラメータ取得 ———
                int viewIdInt = p.Value<int>("viewId");
                int revIdInt = p.Value<int>("revisionId");
                var view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewIdInt)) as View
                                ?? throw new InvalidOperationException($"View not found: {viewIdInt}");
                var revElem = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(revIdInt)) as Autodesk.Revit.DB.Revision
                                ?? throw new InvalidOperationException($"Revision not found: {revIdInt}");

                var sp = view.SketchPlane ?? throw new InvalidOperationException("Target view has no SketchPlane.");
                var pl = sp.GetPlane();
                var origin = pl.Origin; var nx = pl.Normal; var ux = pl.XVec; var uy = pl.YVec;

                var loopsToken = p["curveLoops"] as JArray
                                 ?? throw new ArgumentException("パラメータ 'curveLoops' が必要です。");
                // Accept both shapes: [[{start,end},...]] or [{start,end},...]
                var loopsList = new List<JArray>();
                if (loopsToken.Count > 0 && loopsToken[0] is JObject)
                {
                    var single = new JArray(); foreach (var it in loopsToken) single.Add(it);
                    loopsList.Add(single);
                }
                else
                {
                    foreach (var it in loopsToken) if (it is JArray arr) loopsList.Add(arr);
                }

                var created = new List<object>();

                using (var tx = new Transaction(doc, "Create Revision Clouds"))
                {
                    tx.Start();

                    foreach (var segs in loopsList)
                    {
                        // 1) mm→ft + ビュー平面へ正射影
                        var pts = new List<XYZ>();
                        foreach (JObject seg in segs)
                        {
                            XYZ s = ToInternal(seg["start"]);
                            XYZ e = ToInternal(seg["end"]);
                            pts.Add(ProjectToPlane(s, origin, nx));
                            pts.Add(ProjectToPlane(e, origin, nx));
                        }

                        // 2) 近接端点の統合・閉路化
                        var merged = new List<XYZ>();
                        XYZ last = null;
                        foreach (var q in pts)
                        {
                            if (last != null && last.DistanceTo(q) <= 1e-8) continue;
                            merged.Add(q); last = q;
                        }
                        if (merged.Count >= 2 && !merged.First().IsAlmostEqualTo(merged.Last()))
                            merged.Add(merged.First());
                        if (merged.Count < 5)
                            throw new InvalidOperationException("curveLoops must form a closed polygon (>=4 segments).");

                        // 3) 面内で CW へ正規化
                        var uv = merged.Select(w => ToUV(w, origin, ux, uy)).ToList();
                        if (SignedAreaUV(uv) > 0)
                        {
                            uv.Reverse(); merged.Reverse();
                        }

                        // 4) 曲線列の構築（閉路）
                        var curves = new List<Curve>();
                        for (int i = 0; i < merged.Count - 1; i++)
                        {
                            var a = merged[i]; var b = merged[i + 1];
                            if (a.IsAlmostEqualTo(b)) b = b + nx.Multiply(1e-6); // 退化回避
                            curves.Add(Line.CreateBound(a, b));
                        }

                        // 5) 安全な作図
                        var rc = SafeCreateRevisionCloud(doc, view, Autodesk.Revit.DB.ElementIdCompat.From(revIdInt), curves);
                        created.Add(new { elementId = rc.Id.IntValue() });
                    }

                    tx.Commit();
                }

                return new { ok = true, revisionClouds = created };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message, detail = ex.ToString() };
            }
        }

        // JSON 座標 → 内部単位(ft) の XYZ
        private XYZ ToInternal(JToken point)
        {
            double x = UnitUtils.ConvertToInternalUnits(point.Value<double>("x"), UnitTypeId.Millimeters);
            double y = UnitUtils.ConvertToInternalUnits(point.Value<double>("y"), UnitTypeId.Millimeters);
            double z = UnitUtils.ConvertToInternalUnits(point.Value<double?>("z") ?? 0.0, UnitTypeId.Millimeters);
            return new XYZ(x, y, z);
        }

        private static XYZ ProjectToPlane(XYZ p, XYZ origin, XYZ normal)
        {
            var v = p - origin; var d = v.DotProduct(normal); return p - d * normal;
        }

        private static UV ToUV(XYZ p, XYZ origin, XYZ ux, XYZ uy)
        {
            var v = p - origin; return new UV(v.DotProduct(ux), v.DotProduct(uy));
        }

        private static double SignedAreaUV(IList<UV> uv)
        {
            double a2 = 0; for (int i = 0; i < uv.Count - 1; i++) { var a = uv[i]; var b = uv[i + 1]; a2 += (a.U * b.V - b.U * a.V); } return 0.5 * a2;
        }

        private static Autodesk.Revit.DB.RevisionCloud SafeCreateRevisionCloud(Document doc, View view, ElementId revisionId, IList<Curve> curves)
        {
            // 反射で存在するオーバーロードを解決（Revitバージョン差異を吸収）
            var t = typeof(Autodesk.Revit.DB.RevisionCloud);
            var args1 = new object[] { doc, view, revisionId, curves };
            var args2 = new object[] { doc, view, curves, revisionId };
            var args3 = new object[] { doc, view, curves };

            MethodInfo m;
            // (Document, View, ElementId, IList<Curve>)
            m = t.GetMethod("Create", new[] { typeof(Document), typeof(View), typeof(ElementId), typeof(IList<Curve>) });
            if (m != null) return (Autodesk.Revit.DB.RevisionCloud)m.Invoke(null, args1);

            // (Document, View, IList<Curve>, ElementId)
            m = t.GetMethod("Create", new[] { typeof(Document), typeof(View), typeof(IList<Curve>), typeof(ElementId) });
            if (m != null) return (Autodesk.Revit.DB.RevisionCloud)m.Invoke(null, args2);

            // (Document, View, IList<Curve>) → パラメータでRevision設定
            m = t.GetMethod("Create", new[] { typeof(Document), typeof(View), typeof(IList<Curve>) });
            if (m != null)
            {
                var rc = (Autodesk.Revit.DB.RevisionCloud)m.Invoke(null, args3);
                try
                {
                    // Use REVISION_CLOUD_REVISION which exists across versions
                    var par = rc?.get_Parameter(BuiltInParameter.REVISION_CLOUD_REVISION);
                    if (par != null && !par.IsReadOnly) par.Set(revisionId);
                }
                catch { /* ignore */ }
                return rc;
            }

            throw new InvalidOperationException("No suitable RevisionCloud.Create overload found.");
        }
    }
}


