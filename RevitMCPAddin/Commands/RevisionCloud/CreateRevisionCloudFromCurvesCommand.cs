// ================================================================
// File: Commands/RevisionCloud/CreateRevisionCloudFromCurvesCommand.cs
// Purpose: JSON-RPC "create_revision_cloud" using provided curve loops in view coords (mm)
// Params: { viewId:int, revisionId?:int, curveLoops:[{start:{x,y,z},end:{x,y,z}}[]] }
// Target: .NET Framework 4.8 / Revit 2023
// ================================================================
#nullable enable
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
    public class CreateRevisionCloudFromCurvesCommand : IRevitCommandHandler
    {
        public string CommandName => "create_revision_cloud";

        private static double MmToFt(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);

        private static XYZ FromMm(JObject pt)
        {
            if (pt == null) return XYZ.Zero;
            double x = pt.Value<double?>("x") ?? 0.0;
            double y = pt.Value<double?>("y") ?? 0.0;
            double z = pt.Value<double?>("z") ?? 0.0;
            return new XYZ(MmToFt(x), MmToFt(y), MmToFt(z));
        }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var uidoc = uiapp.ActiveUIDocument;
                var doc = uidoc.Document;
                var p = (JObject)(cmd.Params ?? new JObject());

                // --- View ---
                var viewId = new ElementId(p.Value<int>("viewId"));
                var view = doc.GetElement(viewId) as View;
                if (view == null) return new { ok = false, msg = "view not found" };

                // --- Revision (existing or create new) ---
                ElementId revisionId = ElementId.InvalidElementId;
                JToken rTok;
                if (p.TryGetValue("revisionId", out rTok))
                {
                    var rid = new ElementId(rTok.Value<int>());
                    if (doc.GetElement(rid) is Autodesk.Revit.DB.Revision) revisionId = rid;
                }
                if (revisionId == ElementId.InvalidElementId)
                {
                    using (var txR = new Transaction(doc, "Create Default Revision"))
                    {
                        txR.Start();
                        var rev = Autodesk.Revit.DB.Revision.Create(doc);
                        revisionId = rev.Id;
                        txR.Commit();
                    }
                }

                // --- Parse curveLoops ---
                JToken loopsTok;
                if (!p.TryGetValue("curveLoops", out loopsTok)) return new { ok = false, msg = "curveLoops required" };
                var loops = loopsTok as JArray;
                if (loops == null || loops.Count == 0) return new { ok = false, msg = "curveLoops required" };

                var curveLoops = new List<CurveLoop>();
                foreach (var loopTok in loops)
                {
                    var segs = loopTok as JArray;
                    if (segs == null || segs.Count == 0) continue;

                    var loop = new CurveLoop();
                    XYZ firstStart = null;
                    XYZ lastEnd = null;

                    foreach (var s in segs)
                    {
                        var seg = s as JObject;
                        if (seg == null) continue;
                        var js = seg["start"] as JObject;
                        var je = seg["end"] as JObject;
                        if (js == null || je == null) continue;

                        var a = FromMm(js);
                        var b = FromMm(je);
                        if (!a.IsAlmostEqualTo(b))
                        {
                            loop.Append(Line.CreateBound(a, b));
                            if (firstStart == null) firstStart = a;
                            lastEnd = b;
                        }
                    }

                    // 可能なら明示的に閉じる（Revitは閉ループを期待）
                    if (firstStart != null && lastEnd != null && !firstStart.IsAlmostEqualTo(lastEnd))
                    {
                        loop.Append(Line.CreateBound(lastEnd, firstStart));
                    }

                    if (loop.Count() > 0) curveLoops.Add(loop);
                }
                if (curveLoops.Count == 0) return new { ok = false, msg = "no valid segments" };

                Autodesk.Revit.DB.RevisionCloud rc = null;
                using (var tx = new Transaction(doc, "[MCP] Create Revision Cloud (from curves)"))
                {
                    tx.Start();

                    // 1) まず Revit 2023 に存在する可能性がある CurveLoop オーバーロードを反射で探索
                    //    (バージョンにより存在しないケースがある)
                    var t = typeof(Autodesk.Revit.DB.RevisionCloud);
                    MethodInfo mCurveLoop = t.GetMethod(
                        "Create",
                        new[] { typeof(Document), typeof(View), typeof(IList<CurveLoop>), typeof(ElementId) });

                    if (mCurveLoop != null)
                    {
                        // (Document, View, IList<CurveLoop>, ElementId)
                        rc = (Autodesk.Revit.DB.RevisionCloud)mCurveLoop.Invoke(
                            null, new object[] { doc, view, curveLoops, revisionId });
                    }
                    else
                    {
                        // 2) CurveLoop版が無い場合は、最初のループを Curve[] に落として別オーバーロードを使用
                        var flat = new List<Curve>();
                        foreach (var c in curveLoops.First()) flat.Add(c);

                        // (Document, View, ElementId, IList<Curve>)
                        var m1 = t.GetMethod("Create",
                            new[] { typeof(Document), typeof(View), typeof(ElementId), typeof(IList<Curve>) });

                        if (m1 != null)
                        {
                            rc = (Autodesk.Revit.DB.RevisionCloud)m1.Invoke(
                                null, new object[] { doc, view, revisionId, flat });
                        }
                        else
                        {
                            // (Document, View, IList<Curve>, ElementId)
                            var m2 = t.GetMethod("Create",
                                new[] { typeof(Document), typeof(View), typeof(IList<Curve>), typeof(ElementId) });

                            if (m2 != null)
                            {
                                rc = (Autodesk.Revit.DB.RevisionCloud)m2.Invoke(
                                    null, new object[] { doc, view, flat, revisionId });
                            }
                            else
                            {
                                // (Document, View, IList<Curve>) → 後からパラメータで Revision 設定
                                var m3 = t.GetMethod("Create",
                                    new[] { typeof(Document), typeof(View), typeof(IList<Curve>) });

                                if (m3 != null)
                                {
                                    rc = (Autodesk.Revit.DB.RevisionCloud)m3.Invoke(
                                        null, new object[] { doc, view, flat });

                                    try
                                    {
                                        var par = rc?.get_Parameter(BuiltInParameter.REVISION_CLOUD_REVISION);
                                        if (par != null && !par.IsReadOnly) par.Set(revisionId);
                                    }
                                    catch { /* ignore */ }
                                }
                            }
                        }
                    }

                    tx.Commit();
                }

                if (rc == null) return new { ok = false, msg = "create failed (no suitable overload found)" };
                return new { ok = true, cloudId = rc.Id.IntegerValue, usedRevisionId = revisionId.IntegerValue };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }
}
