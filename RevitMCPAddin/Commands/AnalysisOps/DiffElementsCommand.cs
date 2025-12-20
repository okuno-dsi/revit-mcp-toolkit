#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.AnalysisOps
{
    /// <summary>
    /// diff_elements: Compare two snapshots produced by snapshot_view_elements (or compatible).
    /// Params:
    ///   - left: object (required)
    ///   - right: object (required)
    ///   - keys?: string[] (default: ["familyName","typeName","typeId"])  
    ///   - posTolMm?: number (default 600)
    ///   - lenTolMm?: number (default 150) // reserved
    ///   - includeEndpoints?: bool (default true)
    /// Returns: { ok, modifiedPairs:[{ leftId,rightId,leftCatId,rightCatId,diffs:[{key,left,right}], endpoints? }], leftOnly:int[], rightOnly:int[] }
    /// </summary>
    public class DiffElementsCommand : IRevitCommandHandler
    {
        public string CommandName => "diff_elements";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)(cmd.Params ?? new JObject());
            if (!(p["left"] is JObject left) || !(p["right"] is JObject right))
                return new { ok = false, code = "BAD_SNAPSHOT", msg = "left/right snapshots required." };

            var keys = (p["keys"] as JArray)?.Values<string>()?.Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s.Trim()).ToList() ?? new List<string> { "familyName", "typeName", "typeId" };
            double posTolMm = p.Value<double?>("posTolMm") ?? 600.0;
            double endpointsTolMm = p.Value<double?>("endpointsTolMm") ?? 30.0;
            bool includeEndpoints = p.Value<bool?>("includeEndpoints") ?? true;

            try
            {
                // validate project meta if available
                string lGuid = left.SelectToken("project.guid")?.Value<string>() ?? string.Empty;
                string rGuid = right.SelectToken("project.guid")?.Value<string>() ?? string.Empty;
                string lProj = left.SelectToken("project.name")?.Value<string>() ?? string.Empty;
                string rProj = right.SelectToken("project.name")?.Value<string>() ?? string.Empty;
                // Meta note: GUID mismatch could indicate different documents even with same title
                // We don't hard fail here; comparator is data-driven. Callers can inspect and decide.

                var lelems = (left["elements"] as JArray)?.OfType<JObject>()?.ToList() ?? new List<JObject>();
                var relems = (right["elements"] as JArray)?.OfType<JObject>()?.ToList() ?? new List<JObject>();

                // indices by uniqueId then elementId
                var lByUid = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
                var rByUid = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
                var lById = new Dictionary<int, JObject>();
                var rById = new Dictionary<int, JObject>();

                foreach (var e in lelems)
                {
                    var uid = e.Value<string>("uniqueId") ?? string.Empty;
                    if (!string.IsNullOrEmpty(uid) && !lByUid.ContainsKey(uid)) lByUid[uid] = e;
                    int id = e.Value<int?>("elementId") ?? 0; if (id > 0 && !lById.ContainsKey(id)) lById[id] = e;
                }
                foreach (var e in relems)
                {
                    var uid = e.Value<string>("uniqueId") ?? string.Empty;
                    if (!string.IsNullOrEmpty(uid) && !rByUid.ContainsKey(uid)) rByUid[uid] = e;
                    int id = e.Value<int?>("elementId") ?? 0; if (id > 0 && !rById.ContainsKey(id)) rById[id] = e;
                }

                var paired = new List<(JObject l, JObject r)>();
                var usedL = new HashSet<JObject>();
                var usedR = new HashSet<JObject>();

                // 1) uniqueId pairing
                foreach (var kv in lByUid)
                {
                    if (rByUid.TryGetValue(kv.Key, out var rrow))
                    {
                        paired.Add((kv.Value, rrow)); usedL.Add(kv.Value); usedR.Add(rrow);
                    }
                }

                // 2) elementId pairing for remaining
                foreach (var kv in lById)
                {
                    if (usedL.Contains(kv.Value)) continue;
                    if (rById.TryGetValue(kv.Key, out var rrow) && !usedR.Contains(rrow))
                    {
                        paired.Add((kv.Value, rrow)); usedL.Add(kv.Value); usedR.Add(rrow);
                    }
                }

                // 3) centroid pairing within tolerance for leftovers
                var lLeft = lelems.Where(x => !usedL.Contains(x)).ToList();
                var rLeft = relems.Where(x => !usedR.Contains(x)).ToList();

                static (double x, double y, double z)? CentroidMm(JObject e)
                {
                    var c = e["coordinatesMm"] as JObject; if (c == null) return null;
                    double x = c.Value<double?>("x") ?? double.NaN;
                    double y = c.Value<double?>("y") ?? double.NaN;
                    double z = c.Value<double?>("z") ?? double.NaN;
                    if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z)) return null;
                    return (x, y, z);
                }

                var rLeftByCentroid = new List<(JObject r, (double x, double y, double z) c)>();
                foreach (var r in rLeft)
                {
                    var c = CentroidMm(r); if (c.HasValue) rLeftByCentroid.Add((r, c.Value));
                }

                foreach (var l in lLeft.ToList())
                {
                    var cl = CentroidMm(l); if (!cl.HasValue) continue;
                    (JObject r, double dist)? best = null;
                    foreach (var rr in rLeftByCentroid)
                    {
                        double d = Math.Sqrt(Math.Pow(rr.c.x - cl.Value.x, 2) + Math.Pow(rr.c.y - cl.Value.y, 2) + Math.Pow(rr.c.z - cl.Value.z, 2));
                        if (d <= posTolMm && (best == null || d < best.Value.dist)) best = (rr.r, d);
                    }
                    if (best != null)
                    {
                        paired.Add((l, best.Value.r)); usedL.Add(l); usedR.Add(best.Value.r);
                        rLeftByCentroid.RemoveAll(t => object.ReferenceEquals(t.r, best.Value.r));
                    }
                }

                var modified = new List<object>();
                foreach (var pr in paired)
                {
                    var diffs = CompareRows(pr.l, pr.r, keys);
                    object endpoints = null;
                    if (includeEndpoints)
                    {
                        endpoints = CompareEndpoints(pr.l, pr.r, endpointsTolMm);
                        // If within tolerance, CompareEndpoints returns null
                    }
                    if (diffs.Count > 0 || endpoints != null)
                    {
                        modified.Add(new
                        {
                            leftId = pr.l.Value<int?>("elementId") ?? 0,
                            rightId = pr.r.Value<int?>("elementId") ?? 0,
                            leftCatId = pr.l.Value<int?>("categoryId") ?? 0,
                            rightCatId = pr.r.Value<int?>("categoryId") ?? 0,
                            diffs,
                            endpoints
                        });
                    }
                }

                var leftOnly = lelems.Where(e => !usedL.Contains(e)).Select(e => e.Value<int?>("elementId") ?? 0).Where(i => i > 0).Distinct().ToList();
                var rightOnly = relems.Where(e => !usedR.Contains(e)).Select(e => e.Value<int?>("elementId") ?? 0).Where(i => i > 0).Distinct().ToList();

                return new { ok = true, modifiedPairs = modified, leftOnly, rightOnly };
            }
            catch (Exception ex)
            {
                return new { ok = false, code = "EXCEPTION", msg = ex.Message };
            }
        }

        private static List<object> CompareRows(JObject l, JObject r, List<string> keys)
        {
            var diffs = new List<object>();
            foreach (var k in keys)
            {
                var lv = l[k];
                var rv = r[k];
                if (!JToken.DeepEquals(lv, rv))
                {
                    diffs.Add(new { key = k, left = lv, right = rv });
                }
            }
            return diffs;
        }

        private static object CompareEndpoints(JObject l, JObject r, double tolMm)
        {
            try
            {
                var lw = l.SelectToken("analytic.wire") as JObject;
                var rw = r.SelectToken("analytic.wire") as JObject;
                if (lw == null || rw == null) return null;

                var la = lw["a"] as JObject; var lb = lw["b"] as JObject;
                var ra = rw["a"] as JObject; var rb = rw["b"] as JObject;
                if (la == null || lb == null || ra == null || rb == null) return null;

                (double x, double y, double z) LA = (la.Value<double>("x"), la.Value<double>("y"), la.Value<double>("z"));
                (double x, double y, double z) LB = (lb.Value<double>("x"), lb.Value<double>("y"), lb.Value<double>("z"));
                (double x, double y, double z) RA = (ra.Value<double>("x"), ra.Value<double>("y"), ra.Value<double>("z"));
                (double x, double y, double z) RB = (rb.Value<double>("x"), rb.Value<double>("y"), rb.Value<double>("z"));

                double D((double x, double y, double z) p, (double x, double y, double z) q)
                {
                    return Math.Sqrt(Math.Pow(p.x - q.x, 2) + Math.Pow(p.y - q.y, 2) + Math.Pow(p.z - q.z, 2));
                }

                var aa_bb = (D(LA, RA), D(LB, RB));
                var ab_ba = (D(LA, RB), D(LB, RA));

                double sum1 = aa_bb.Item1 + aa_bb.Item2;
                double sum2 = ab_ba.Item1 + ab_ba.Item2;

                if (double.IsNaN(sum1) || double.IsNaN(sum2)) return null;

                // choose best pairing
                bool aaBb = sum1 <= sum2;
                double dA = aaBb ? aa_bb.Item1 : ab_ba.Item1;
                double dB = aaBb ? aa_bb.Item2 : ab_ba.Item2;

                // apply tolerance: if both endpoints are within tol, treat as no meaningful change
                if (dA <= tolMm && dB <= tolMm) return null;

                return aaBb
                    ? (object)new { mode = "AA_BB", endA_mm = Math.Round(dA, 3), endB_mm = Math.Round(dB, 3) }
                    : (object)new { mode = "AB_BA", endA_mm = Math.Round(dA, 3), endB_mm = Math.Round(dB, 3) };
            }
            catch { return null; }
        }
    }
}
