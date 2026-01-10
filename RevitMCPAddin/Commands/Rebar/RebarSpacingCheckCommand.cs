// ================================================================
// Command: rebar_spacing_check
// Purpose: Measure actual center-to-center spacing between existing
//          rebars in the model and validate against the clearance table.
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// Kind   : read
// ================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.Core.Rebar;

namespace RevitMCPAddin.Commands.Rebar
{
    [RpcCommand("rebar_spacing_check",
        Category = "Rebar",
        Kind = "read",
        Risk = RiskLevel.Low,
        Summary = "Measure actual center-to-center spacing between existing rebars (model geometry) and validate against the clearance table (RebarBarClearanceTable.json).",
        ExampleJsonRpc = "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"rebar_spacing_check\", \"params\":{ \"useSelectionIfEmpty\":true, \"filter\":{ \"commentsTagEquals\":\"RevitMcp:AutoRebar\" }, \"maxPairs\":20000, \"includePairs\":false } }"
    )]
    public sealed class RebarSpacingCheckCommand : IRevitCommandHandler
    {
        public string CommandName => "rebar_spacing_check";

        private sealed class SingleBar
        {
            public int rebarId;
            public string uniqueId = string.Empty;
            public string barTypeName = string.Empty;
            public int diaMm;
            public XYZ mid = XYZ.Zero;
            public XYZ dir = XYZ.BasisX;
        }

        private static bool TryGetSingleBarRepresentation(Document doc, Autodesk.Revit.DB.Structure.Rebar rebar, out SingleBar bar, out string reason)
        {
            bar = null;
            reason = string.Empty;
            if (doc == null || rebar == null) { reason = "NO_REBAR"; return false; }

            try
            {
                // Only treat as "single" bar when it is not an array/set.
                try
                {
                    if (rebar.NumberOfBarPositions > 1)
                    {
                        reason = "SET_REBAR";
                        return false;
                    }
                }
                catch { /* ignore */ }

                IList<Curve> curves = null;
                try
                {
                    curves = rebar.GetCenterlineCurves(
                        false,
                        false,
                        false,
                        MultiplanarOption.IncludeAllMultiplanarCurves,
                        0);
                }
                catch
                {
                    curves = null;
                }

                if (curves == null || curves.Count == 0) { reason = "NO_CURVES"; return false; }

                Curve longest = null;
                double bestLen = 0.0;
                foreach (var c in curves)
                {
                    if (c == null) continue;
                    double len = 0.0;
                    try { len = c.Length; } catch { len = 0.0; }
                    if (len > bestLen)
                    {
                        bestLen = len;
                        longest = c;
                    }
                }

                if (longest == null || bestLen < 1e-6) { reason = "NO_LONG_CURVE"; return false; }

                XYZ p0, p1;
                try
                {
                    p0 = longest.GetEndPoint(0);
                    p1 = longest.GetEndPoint(1);
                }
                catch
                {
                    reason = "CURVE_ENDPOINT_FAILED";
                    return false;
                }

                var v = p1 - p0;
                if (v.GetLength() < 1e-9) { reason = "ZERO_DIR"; return false; }
                var dir = v.Normalize();
                var mid = (p0 + p1) * 0.5;

                string barTypeName = string.Empty;
                int diaMm = 0;

                try
                {
                    var tid = rebar.GetTypeId();
                    var bt = tid != null && tid != ElementId.InvalidElementId ? doc.GetElement(tid) as RebarBarType : null;
                    if (bt != null)
                    {
                        barTypeName = (bt.Name ?? string.Empty).Trim();
                        try
                        {
                            var mm = UnitHelper.FtToMm(bt.BarModelDiameter);
                            diaMm = (int)Math.Round(mm);
                        }
                        catch { diaMm = 0; }
                    }
                }
                catch { /* ignore */ }

                int rid = 0;
                try { rid = rebar.Id.IntValue(); } catch { rid = 0; }

                bar = new SingleBar
                {
                    rebarId = rid,
                    uniqueId = rebar.UniqueId ?? string.Empty,
                    barTypeName = barTypeName,
                    diaMm = diaMm,
                    mid = mid,
                    dir = dir
                };
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        private static double PerpDistanceMm(XYZ dirUnit, XYZ aMid, XYZ bMid)
        {
            var d = bMid - aMid;
            var proj = dirUnit.Multiply(d.DotProduct(dirUnit));
            var perp = d - proj;
            return UnitHelper.FtToMm(perp.GetLength());
        }

        private static ElementId TryGetRebarHostId(Autodesk.Revit.DB.Structure.Rebar rebar)
        {
            if (rebar == null) return ElementId.InvalidElementId;
            try
            {
                var mi = rebar.GetType().GetMethod("GetHostId", Type.EmptyTypes);
                if (mi != null)
                {
                    var obj = mi.Invoke(rebar, null);
                    if (obj is ElementId id) return id;
                }
            }
            catch { /* ignore */ }
            return ElementId.InvalidElementId;
        }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。", "NO_DOC");

            var p = cmd.Params as JObject ?? new JObject();

            // parameters
            int maxPairs = Math.Max(0, p.Value<int?>("maxPairs") ?? 20000);
            bool includePairs = p.Value<bool?>("includePairs") ?? false;
            int pairLimit = Math.Max(0, p.Value<int?>("pairLimit") ?? 50);
            double parallelDotMin = p.Value<double?>("parallelDotMin") ?? 0.985;
            if (parallelDotMin < 0.0) parallelDotMin = 0.0;
            if (parallelDotMin > 1.0) parallelDotMin = 1.0;

            string commentsTagEquals = string.Empty;
            try
            {
                var f = p["filter"] as JObject;
                if (f != null) commentsTagEquals = (f.Value<string>("commentsTagEquals") ?? string.Empty).Trim();
            }
            catch { /* ignore */ }

            // ids
            var hostIds = new List<int>();
            var rebarIds = new List<int>();
            try
            {
                var arrH = p["hostElementIds"] as JArray;
                if (arrH != null)
                {
                    foreach (var t in arrH)
                    {
                        if (t == null || t.Type != JTokenType.Integer) continue;
                        int v = t.Value<int>();
                        if (v > 0) hostIds.Add(v);
                    }
                }
            }
            catch { /* ignore */ }

            try
            {
                foreach (var key in new[] { "rebarElementIds", "elementIds" })
                {
                    var arr = p[key] as JArray;
                    if (arr == null) continue;
                    foreach (var t in arr)
                    {
                        if (t == null) continue;
                        if (t.Type == JTokenType.Integer)
                        {
                            int v = t.Value<int>();
                            if (v > 0) rebarIds.Add(v);
                        }
                        else if (t.Type == JTokenType.Float)
                        {
                            int v = (int)t.Value<double>();
                            if (v > 0) rebarIds.Add(v);
                        }
                        else if (t.Type == JTokenType.String && int.TryParse(t.Value<string>(), out var v2))
                        {
                            if (v2 > 0) rebarIds.Add(v2);
                        }
                    }
                }
            }
            catch { /* ignore */ }

            bool useSelectionIfEmpty = p.Value<bool?>("useSelectionIfEmpty") ?? true;
            if (useSelectionIfEmpty && hostIds.Count == 0 && rebarIds.Count == 0 && uidoc != null)
            {
                try
                {
                    foreach (var id in uidoc.Selection.GetElementIds())
                    {
                        int v = 0;
                        try { v = id.IntValue(); } catch { v = 0; }
                        if (v <= 0) continue;
                        var e = doc.GetElement(id);
                        if (e is Autodesk.Revit.DB.Structure.Rebar) rebarIds.Add(v);
                        else hostIds.Add(v);
                    }
                }
                catch { /* ignore */ }
            }

            var tableStatus = RebarBarClearanceTableService.GetStatus();

            // Collect rebars per host group
            var groups = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase); // hostUniqueId -> rebarIds
            var hostMeta = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);

            void AddRebarToGroup(Element host, int rebarId)
            {
                if (host == null || rebarId <= 0) return;
                var key = host.UniqueId ?? string.Empty;
                if (key.Length == 0) key = "host:" + host.Id.IntValue().ToString();
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    groups[key] = list;
                    hostMeta[key] = new JObject
                    {
                        ["hostElementId"] = host.Id.IntValue(),
                        ["hostUniqueId"] = host.UniqueId ?? string.Empty,
                        ["categoryName"] = host.Category != null ? (host.Category.Name ?? string.Empty) : string.Empty
                    };
                }
                list.Add(rebarId);
            }

            if (hostIds.Count > 0)
            {
                foreach (var hid in hostIds.Distinct())
                {
                    var host = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(hid));
                    if (host == null) continue;

                    bool validHost = false;
                    try { validHost = RebarHostData.IsValidHost(host); } catch { validHost = false; }
                    if (!validHost) continue;

                    RebarHostData hd = null;
                    try { hd = RebarHostData.GetRebarHostData(host); } catch { hd = null; }
                    if (hd == null) continue;

                    var rebarIdList = new List<ElementId>();
                    try
                    {
                        var mi = hd.GetType().GetMethod("GetRebarsInHost", Type.EmptyTypes);
                        var obj = mi != null ? mi.Invoke(hd, null) : null;
                        if (obj is System.Collections.IEnumerable en)
                        {
                            foreach (var x in en)
                            {
                                if (x is ElementId eid) rebarIdList.Add(eid);
                                else if (x is Autodesk.Revit.DB.Structure.Rebar rb) rebarIdList.Add(rb.Id);
                            }
                        }
                    }
                    catch { /* ignore */ }

                    foreach (var rid in rebarIdList)
                    {
                        int intId = 0;
                        try { intId = rid.IntValue(); } catch { intId = 0; }
                        if (intId <= 0) continue;

                        if (!string.IsNullOrWhiteSpace(commentsTagEquals))
                        {
                            try
                            {
                                var rebar = doc.GetElement(rid) as Autodesk.Revit.DB.Structure.Rebar;
                                if (rebar == null) continue;
                                var c = rebar.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                var s = c != null ? c.AsString() : null;
                                if (!string.Equals((s ?? string.Empty).Trim(), commentsTagEquals, StringComparison.OrdinalIgnoreCase))
                                    continue;
                            }
                            catch { /* ignore */ }
                        }

                        AddRebarToGroup(host, intId);
                    }
                }
            }
            else
            {
                // Rebar ids directly (group by host if possible)
                foreach (var rid in rebarIds.Distinct())
                {
                    var rebar = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(rid)) as Autodesk.Revit.DB.Structure.Rebar;
                    if (rebar == null) continue;

                    if (!string.IsNullOrWhiteSpace(commentsTagEquals))
                    {
                        try
                        {
                            var c = rebar.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                            var s = c != null ? c.AsString() : null;
                            if (!string.Equals((s ?? string.Empty).Trim(), commentsTagEquals, StringComparison.OrdinalIgnoreCase))
                                continue;
                        }
                        catch { /* ignore */ }
                    }

                    Element host = null;
                    try
                    {
                        var hid = TryGetRebarHostId(rebar);
                        host = hid != null && hid != ElementId.InvalidElementId ? doc.GetElement(hid) : null;
                    }
                    catch { host = null; }
                    if (host == null)
                    {
                        // fallback: keep as "unknown host" group
                        var key = "host:unknown";
                        if (!groups.TryGetValue(key, out var list))
                        {
                            list = new List<int>();
                            groups[key] = list;
                            hostMeta[key] = new JObject
                            {
                                ["hostElementId"] = null,
                                ["hostUniqueId"] = null,
                                ["categoryName"] = null
                            };
                        }
                        list.Add(rid);
                    }
                    else
                    {
                        AddRebarToGroup(host, rid);
                    }
                }
            }

            var results = new JArray();

            foreach (var kv in groups)
            {
                var key = kv.Key;
                var ids = kv.Value.Distinct().ToList();

                var hostObj = hostMeta.ContainsKey(key) ? (JObject)hostMeta[key].DeepClone() : new JObject();
                hostObj["rebarCount"] = ids.Count;

                var singleBars = new List<SingleBar>();
                var skipped = new JArray();

                foreach (var rid in ids)
                {
                    var rebar = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(rid)) as Autodesk.Revit.DB.Structure.Rebar;
                    if (rebar == null)
                    {
                        skipped.Add(new JObject { ["rebarElementId"] = rid, ["reason"] = "NOT_FOUND" });
                        continue;
                    }

                    if (TryGetSingleBarRepresentation(doc, rebar, out var bar, out var reason))
                    {
                        if (bar != null) singleBars.Add(bar);
                    }
                    else
                    {
                        skipped.Add(new JObject { ["rebarElementId"] = rid, ["reason"] = reason });
                    }
                }

                hostObj["singleBarsCount"] = singleBars.Count;
                hostObj["skipped"] = skipped;

                // Pairwise spacing check for single bars (parallel lines).
                long analyzedPairs = 0;
                long skippedPairs = 0;
                double minDistMm = 0.0;
                bool hasMin = false;

                var violations = new List<JObject>();
                var violatingRebarIds = new HashSet<int>();

                int n = singleBars.Count;
                for (int i = 0; i < n; i++)
                {
                    var a = singleBars[i];
                    var aDir = a.dir;
                    if (aDir.GetLength() < 1e-9) continue;

                    for (int j = i + 1; j < n; j++)
                    {
                        if (maxPairs > 0 && analyzedPairs >= maxPairs) { skippedPairs++; continue; }

                        var b = singleBars[j];
                        var bDir = b.dir;
                        if (bDir.GetLength() < 1e-9) { skippedPairs++; continue; }

                        double dot = Math.Abs(aDir.DotProduct(bDir));
                        if (dot < parallelDotMin)
                        {
                            skippedPairs++;
                            continue;
                        }

                        analyzedPairs++;

                        double dist = 0.0;
                        try { dist = PerpDistanceMm(aDir, a.mid, b.mid); }
                        catch { dist = 0.0; }

                        if (dist > 0.0)
                        {
                            if (!hasMin) { minDistMm = dist; hasMin = true; }
                            else minDistMm = Math.Min(minDistMm, dist);
                        }

                        double reqA = 0.0;
                        double reqB = 0.0;
                        try { if (a.diaMm > 0) RebarBarClearanceTableService.TryGetCenterToCenterMm(a.diaMm, out reqA); } catch { reqA = 0.0; }
                        try { if (b.diaMm > 0) RebarBarClearanceTableService.TryGetCenterToCenterMm(b.diaMm, out reqB); } catch { reqB = 0.0; }

                        double req = Math.Max(reqA, reqB);
                        if (req <= 0.0) continue;

                        bool ok = dist + 1e-6 >= req;
                        if (!ok)
                        {
                            if (a.rebarId > 0) violatingRebarIds.Add(a.rebarId);
                            if (b.rebarId > 0) violatingRebarIds.Add(b.rebarId);

                            var v = new JObject
                            {
                                ["a"] = new JObject
                                {
                                    ["rebarElementId"] = a.rebarId,
                                    ["barTypeName"] = a.barTypeName,
                                    ["diaMm"] = a.diaMm,
                                    ["requiredCcMm"] = reqA > 0.0 ? reqA : (double?)null
                                },
                                ["b"] = new JObject
                                {
                                    ["rebarElementId"] = b.rebarId,
                                    ["barTypeName"] = b.barTypeName,
                                    ["diaMm"] = b.diaMm,
                                    ["requiredCcMm"] = reqB > 0.0 ? reqB : (double?)null
                                },
                                ["distanceMm"] = Math.Round(dist, 3),
                                ["requiredPairCcMm"] = Math.Round(req, 3),
                                ["shortageMm"] = Math.Round(req - dist, 3)
                            };
                            violations.Add(v);
                        }
                    }
                }

                hostObj["pairwise"] = new JObject
                {
                    ["parallelDotMin"] = parallelDotMin,
                    ["maxPairs"] = maxPairs,
                    ["analyzedPairs"] = analyzedPairs,
                    ["skippedPairs"] = skippedPairs,
                    ["minDistanceMm"] = hasMin ? Math.Round(minDistMm, 3) : (double?)null,
                    ["violationsCount"] = violations.Count
                };

                hostObj["violatingRebarCount"] = violatingRebarIds.Count;
                hostObj["violatingRebarIds"] = new JArray(violatingRebarIds.OrderBy(x => x).ToArray());

                if (includePairs)
                {
                    var worst = violations
                        .OrderByDescending(x => x.Value<double?>("shortageMm") ?? 0.0)
                        .Take(pairLimit)
                        .ToArray();
                    hostObj["violations"] = new JArray(worst);
                }

                results.Add(hostObj);
            }

            return ResultUtil.Ok(new
            {
                ok = true,
                clearanceTable = tableStatus,
                groups = results,
                msg = "OK"
            });
        }
    }
}
