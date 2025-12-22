#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.SiteOps
{
    /// <summary>
    /// 実装コマンド（5つ）:
    ///  - get_topography_info           : { "topographyId": int }
    ///  - list_topographies             : {}
    ///  - get_site_subregions           : { "topographyId?": int }
    ///  - set_topography_material       : { "topographyId": int, "materialName": string }
    ///  - set_subregion_material        : { "subregionId": int,  "materialName": string }
    /// 返却は { ok, ... } / エラーは { ok:false, msg } の方針に準拠。
    /// 単位：入出力は mm（内部 ft に変換）。
    /// </summary>
    public sealed class TopographyInfoHandler : IRevitCommandHandler
    {
        public string CommandName =>
            "get_topography_info|list_topographies|get_site_subregions|set_topography_material|set_subregion_material";

        public object Execute(UIApplication uiapp, RequestCommand request)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            try
            {
                var p = request.Params as JObject ?? new JObject();

                switch (request.Method)
                {
                    case "get_topography_info": return GetTopographyInfo(doc, p);
                    case "list_topographies": return ListTopographies(doc);
                    case "get_site_subregions": return GetSiteSubregions(doc, p);
                    case "set_topography_material": return SetTopographyMaterial(doc, p);
                    case "set_subregion_material": return SetSubregionMaterial(doc, p);
                }
                return new { ok = false, msg = "Unknown method." };
            }
            catch (Exception ex)
            {
                LoggerProxy.Error("[Site] topo info/material error: " + ex);
                return new { ok = false, msg = ex.Message };
            }
        }

        // ---------------------------------------------------------------------
        // 1) get_topography_info
        // ---------------------------------------------------------------------
        private object GetTopographyInfo(Document doc, JObject p)
        {
            int topoId = p.Value<int>("topographyId");
            if (topoId <= 0) return new { ok = false, msg = "topographyId is required." };

            var topo = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(topoId)) as TopographySurface;
            if (topo == null) return new { ok = false, msg = $"TopographySurface not found: {topoId}" };

            string name = SafeGetName(topo);
            string materialName = TryGetMaterialName(topo);

            var pts = topo.GetPoints() ?? new List<XYZ>();
            var ptsMm = pts.Select(q => new { x = UnitHelper.FtToMm(q.X), y = UnitHelper.FtToMm(q.Y), z = UnitHelper.FtToMm(q.Z) }).ToList();

            return new
            {
                ok = true,
                elementId = topo.Id.IntValue(),
                name,
                pointCount = ptsMm.Count,
                pointsMm = ptsMm,
                material = string.IsNullOrEmpty(materialName) ? null : materialName
            };
        }

        // ---------------------------------------------------------------------
        // 2) list_topographies
        // ---------------------------------------------------------------------
        private object ListTopographies(Document doc)
        {
            var list = new List<object>();
            var all = new FilteredElementCollector(doc).OfClass(typeof(TopographySurface)).Cast<TopographySurface>();
            foreach (var t in all)
            {
                int count = 0;
                try { count = t.GetPoints()?.Count ?? 0; } catch { /* ignore */ }
                list.Add(new { id = t.Id.IntValue(), name = SafeGetName(t), pointCount = count });
            }
            return new { ok = true, count = list.Count, items = list };
        }

        // ---------------------------------------------------------------------
        // 3) get_site_subregions
        // ---------------------------------------------------------------------
        private object GetSiteSubregions(Document doc, JObject p)
        {
            int topoIdFilter = p.Value<int?>("topographyId") ?? 0;

            // 直参照せずに型名で検出（環境差対策）
            var allElems = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToElements();

            var subregions = allElems.Where(e => string.Equals(e.GetType().Name, "SiteSubRegion", StringComparison.OrdinalIgnoreCase));

            // トポ面による絞り込み（TopographySurface プロパティがあれば利用）
            if (topoIdFilter > 0)
            {
                subregions = subregions.Where(e =>
                {
                    try
                    {
                        var prop = e.GetType().GetProperty("TopographySurface", BindingFlags.Instance | BindingFlags.Public);
                        if (prop == null) return false;
                        var topoObj = prop.GetValue(e, null) as Element;
                        return topoObj != null && topoObj.Id.IntValue() == topoIdFilter;
                    }
                    catch { return false; }
                });
            }

            var items = new List<object>();

            foreach (var sr in subregions)
            {
                string mname = TryGetMaterialName(sr); // Element 扱いなのでそのままOK

                // 1) まずは GetBoundaryLoops() を反射で試す
                var loopsMm = new List<List<object>>();
                bool gotLoops = false;
                try
                {
                    var m = sr.GetType().GetMethod("GetBoundaryLoops", BindingFlags.Instance | BindingFlags.Public);
                    if (m != null)
                    {
                        var loopsObj = m.Invoke(sr, null) as System.Collections.IEnumerable;
                        if (loopsObj != null)
                        {
                            foreach (var loopObj in loopsObj)
                            {
                                // loopObj は CurveLoop
                                var loop = loopObj as CurveLoop;
                                if (loop == null) continue;
                                var edges = loop.ToList();
                                var pts = new List<object>();
                                for (int i = 0; i < edges.Count; i++)
                                {
                                    var p0 = edges[i].GetEndPoint(0);
                                    pts.Add(new { x = UnitHelper.FtToMm(p0.X), y = UnitHelper.FtToMm(p0.Y) });
                                }
                                if (edges.Count > 0)
                                {
                                    var pFirst = edges[0].GetEndPoint(0);
                                    pts.Add(new { x = UnitHelper.FtToMm(pFirst.X), y = UnitHelper.FtToMm(pFirst.Y) });
                                }
                                loopsMm.Add(pts);
                            }
                            gotLoops = true;
                        }
                    }
                }
                catch { /* ignore */ }

                // 2) フォールバック：ジオメトリから2D近似輪郭（簡易）
                if (!gotLoops)
                {
                    try
                    {
                        var opt = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false, IncludeNonVisibleObjects = true };
                        var geo = sr.get_Geometry(opt);
                        var curveLists = new List<List<Curve>>();

                        foreach (var obj in geo)
                        {
                            if (obj is Curve curve)
                            {
                                curveLists.Add(new List<Curve> { curve });
                            }
                            else if (obj is GeometryInstance gi)
                            {
                                var inst = gi.GetInstanceGeometry();
                                foreach (var g in inst)
                                {
                                    if (g is Curve c2) curveLists.Add(new List<Curve> { c2 });
                                }
                            }
                        }

                        // 非常に簡易：曲線群を連結できる範囲でまとめて点列化
                        foreach (var cl in curveLists)
                        {
                            var pts = new List<object>();
                            foreach (var c in cl)
                            {
                                var p0 = c.GetEndPoint(0);
                                pts.Add(new { x = UnitHelper.FtToMm(p0.X), y = UnitHelper.FtToMm(p0.Y) });
                            }
                            if (cl.Count > 0)
                            {
                                var pFirst = cl[0].GetEndPoint(0);
                                pts.Add(new { x = UnitHelper.FtToMm(pFirst.X), y = UnitHelper.FtToMm(pFirst.Y) });
                            }
                            if (pts.Count >= 3) loopsMm.Add(pts);
                        }
                    }
                    catch { /* ignore */ }
                }

                items.Add(new
                {
                    id = sr.Id.IntValue(),
                    material = string.IsNullOrEmpty(mname) ? null : mname,
                    loopsMm
                });
            }

            return new { ok = true, count = items.Count, items };
        }


        // ---------------------------------------------------------------------
        // 4) set_topography_material
        // ---------------------------------------------------------------------
        private object SetTopographyMaterial(Document doc, JObject p)
        {
            int topoId = p.Value<int>("topographyId");
            string materialName = p.Value<string>("materialName") ?? "";
            if (topoId <= 0) return new { ok = false, msg = "topographyId is required." };
            if (string.IsNullOrWhiteSpace(materialName)) return new { ok = false, msg = "materialName is required." };

            var topo = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(topoId));
            if (topo == null) return new { ok = false, msg = $"TopographySurface not found: {topoId}" };

            var mat = FindMaterialByName(doc, materialName);
            if (mat == null) return new { ok = false, msg = $"Material not found: '{materialName}'" };

            using (var t = new Transaction(doc, "Set Topography Material"))
            {
                t.Start();
                bool ok = TrySetMaterial(topo, mat.Id);
                t.Commit();
                if (!ok) return new { ok = false, msg = "Failed to assign material to TopographySurface." };
            }
            LoggerProxy.Info($"[Site] Topography material set: topo={topoId}, material='{materialName}'");
            return new { ok = true };
        }

        // ---------------------------------------------------------------------
        // 5) set_subregion_material
        // ---------------------------------------------------------------------
        private object SetSubregionMaterial(Document doc, JObject p)
        {
            int subId = p.Value<int>("subregionId");
            string materialName = p.Value<string>("materialName") ?? "";
            if (subId <= 0) return new { ok = false, msg = "subregionId is required." };
            if (string.IsNullOrWhiteSpace(materialName)) return new { ok = false, msg = "materialName is required." };

            var sr = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(subId));
            if (sr == null) return new { ok = false, msg = $"SiteSubRegion not found: {subId}" };

            var mat = FindMaterialByName(doc, materialName);
            if (mat == null) return new { ok = false, msg = $"Material not found: '{materialName}'" };

            using (var t = new Transaction(doc, "Set Site SubRegion Material"))
            {
                t.Start();
                bool ok = TrySetMaterial(sr, mat.Id);
                t.Commit();
                if (!ok) return new { ok = false, msg = "Failed to assign material to SiteSubRegion." };
            }
            LoggerProxy.Info($"[Site] SubRegion material set: subregion={subId}, material='{materialName}'");
            return new { ok = true };
        }

        // =====================================================================
        // helpers
        // =====================================================================
        private static string SafeGetName(Element e)
        {
            try { return e.Name ?? ""; } catch { return ""; }
        }

        private static Material FindMaterialByName(Document doc, string name)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>()
                .FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static string TryGetMaterialName(Element e)
        {
            // 1) 汎用：MATERIAL_ID_PARAM
            try
            {
                var prm = e.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (prm != null && prm.StorageType == StorageType.ElementId)
                {
                    var id = prm.AsElementId();
                    if (id != null && id.IntValue() > 0)
                    {
                        var mat = e.Document.GetElement(id) as Material;
                        if (mat != null) return mat.Name;
                    }
                }
            }
            catch { /* ignore */ }

            // 2) プロパティ MaterialId（TopographySurface / SiteSubRegion など）
            try
            {
                var prop = e.GetType().GetProperty("MaterialId", BindingFlags.Instance | BindingFlags.Public);
                if (prop != null && prop.CanRead)
                {
                    var eid = prop.GetValue(e, null) as ElementId;
                    if (eid != null && eid.IntValue() > 0)
                    {
                        var mat = e.Document.GetElement(eid) as Material;
                        if (mat != null) return mat.Name;
                    }
                }
            }
            catch { /* ignore */ }

            return "";
        }

        private static bool TrySetMaterial(Element e, ElementId materialId)
        {
            // 1) 汎用：MATERIAL_ID_PARAM を優先
            try
            {
                var prm = e.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (prm != null && !prm.IsReadOnly && prm.StorageType == StorageType.ElementId)
                {
                    return prm.Set(materialId);
                }
            }
            catch { /* try property */ }

            // 2) プロパティ MaterialId セッター（環境によっては存在）
            try
            {
                var prop = e.GetType().GetProperty("MaterialId", BindingFlags.Instance | BindingFlags.Public);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(e, materialId, null);
                    return true;
                }
            }
            catch { /* ignore */ }

            return false;
        }

        // Logger の依存を避ける安全ラッパ
        private static class LoggerProxy
        {
            static readonly Type T = typeof(RevitMCPAddin.Core.RevitLogger);
            static readonly MethodInfo MInfo =
                T.GetMethod("Info") ?? T.GetMethod("LogInfo") ?? T.GetMethod("AppendLog");
            static readonly MethodInfo MWarn =
                T.GetMethod("Warn") ?? T.GetMethod("LogWarn") ?? T.GetMethod("AppendLog");
            static readonly MethodInfo MErr =
                T.GetMethod("Error") ?? T.GetMethod("LogError") ?? T.GetMethod("AppendLog");

            public static void Info(string msg) { if (MInfo != null) MInfo.Invoke(null, new object[] { msg }); else System.Diagnostics.Debug.WriteLine(msg); }
            public static void Warn(string msg) { if (MWarn != null) MWarn.Invoke(null, new object[] { msg }); else System.Diagnostics.Debug.WriteLine("WARN: " + msg); }
            public static void Error(string msg) { if (MErr != null) MErr.Invoke(null, new object[] { msg }); else System.Diagnostics.Debug.WriteLine("ERROR: " + msg); }
        }
    }
}


