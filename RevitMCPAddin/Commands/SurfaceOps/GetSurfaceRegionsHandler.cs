#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.SurfaceOps
{
    /// <summary>
    /// Floor / Roof / Ceiling / Wall / Generic を一つのAPIで扱う上位集計版。
    /// 親PlanarFaceを確定 → 親.GetRegions() でサブフェイス直列挙 → REGON塗装 / 親面丸塗り / 親面ごと未塗装残差。
    /// </summary>
    public class GetSurfaceRegionsHandler : IRevitCommandHandler
    {
        public string CommandName => "get_surface_regions";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return Err("アクティブドキュメントがありません。");
            var p = cmd.Params as JObject ?? new JObject();

            try
            {
                // ターゲット要素
                Element elem = null;
                var idTok = p["elementId"];
                var uidTok = p["uniqueId"];
                if (idTok != null && idTok.Type == JTokenType.Integer)
                    elem = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(idTok.Value<int>()));
                else if (uidTok != null && uidTok.Type == JTokenType.String)
                    elem = doc.GetElement(uidTok.Value<string>());
                if (elem == null) return Err("要素が見つかりません。");

                // hostKind / side の既定
                string hostKind = (p.Value<string>("hostKind") ?? "auto").Trim().ToLowerInvariant();
                if (hostKind == "auto")
                {
                    hostKind = (elem is Autodesk.Revit.DB.Floor) ? "floor"
                             : (elem is RoofBase) ? "roof"
                             : (elem is Ceiling) ? "ceiling"
                             : (elem is Wall) ? "wall"
                             : "generic";
                }
                string side = p.Value<string>("side");
                if (string.IsNullOrEmpty(side))
                {
                    side = hostKind == "ceiling" ? "Bottom" :
                           hostKind == "wall" ? "Exterior" :
                           "Top";
                }

                bool includePainted = p.Value<bool?>("includePainted") ?? true;
                bool includeUnpainted = p.Value<bool?>("includeUnpainted") ?? true;

                // 親面の抽出
                IList<Reference> parentRefs;
                string strategy;
                if (!TryGetParents(doc, elem, hostKind, side, p, out parentRefs, out strategy))
                    return Err("該当面が取得できません。");

                var regions = new JArray();
                var parentAreasFt2 = new Dictionary<string, double>(StringComparer.Ordinal);
                var regionPaintedFt2 = new Dictionary<string, double>(StringComparer.Ordinal);
                var parentWholePaintFt2 = new Dictionary<string, double>(StringComparer.Ordinal);

                // 親→子(GetRegions) 直列挙
                foreach (var pr in parentRefs)
                {
                    var head = pr.ConvertToStableRepresentation(doc) ?? string.Empty;
                    if (string.IsNullOrEmpty(head)) continue;

                    var parentFace = elem.GetGeometryObjectFromReference(pr) as PlanarFace;
                    if (parentFace == null) continue;

                    parentAreasFt2[head] = parentAreasFt2.ContainsKey(head) ? parentAreasFt2[head] + parentFace.Area : parentFace.Area;

                    var subs = parentFace.HasRegions
                        ? (parentFace.GetRegions()?.OfType<Face>().ToList() ?? new List<Face>())
                        : new List<Face> { parentFace };

                    foreach (var sf in subs)
                    {
                        var sr = sf?.Reference?.ConvertToStableRepresentation(doc) ?? string.Empty;
                        if (string.IsNullOrEmpty(sr)) continue;

                        if (includePainted && doc.IsPainted(elem.Id, sf))
                        {
                            var mid = doc.GetPaintedMaterial(elem.Id, sf);
                            var mat = (mid == ElementId.InvalidElementId) ? null : doc.GetElement(mid) as Material;

                            regions.Add(new JObject
                            {
                                ["faceRef"] = sr,
                                ["isRegion"] = parentFace.HasRegions && !ReferenceEquals(sf, parentFace),
                                ["isPainted"] = true,
                                ["area_m2"] = UnitHelper.Ft2ToM2(sf.Area),
                                ["materialId"] = mid.IntValue(),
                                ["materialName"] = mat?.Name ?? ""
                            });

                            double cur; regionPaintedFt2.TryGetValue(head, out cur);
                            regionPaintedFt2[head] = cur + sf.Area;
                        }
                    }

                    if (includePainted && doc.IsPainted(elem.Id, parentFace))
                    {
                        var mid = doc.GetPaintedMaterial(elem.Id, parentFace);
                        var mat = (mid == ElementId.InvalidElementId) ? null : doc.GetElement(mid) as Material;

                        regions.Add(new JObject
                        {
                            ["faceRef"] = head,
                            ["isRegion"] = false,
                            ["isPainted"] = true,
                            ["area_m2"] = UnitHelper.Ft2ToM2(parentFace.Area),
                            ["materialId"] = mid.IntValue(),
                            ["materialName"] = mat?.Name ?? ""
                        });

                        parentWholePaintFt2[head] = parentWholePaintFt2.ContainsKey(head)
                            ? parentWholePaintFt2[head] + parentFace.Area
                            : parentFace.Area;
                    }
                }

                if (includeUnpainted)
                {
                    foreach (var kv in parentAreasFt2)
                    {
                        var head = kv.Key;
                        var total = kv.Value;
                        double reg = 0.0; regionPaintedFt2.TryGetValue(head, out reg);
                        double whole = 0.0; parentWholePaintFt2.TryGetValue(head, out whole);
                        var residual = Math.Max(0.0, total - (reg + whole));
                        if (residual > 1e-8)
                        {
                            regions.Add(new JObject
                            {
                                ["faceRef"] = head,
                                ["isRegion"] = false,
                                ["isPainted"] = false,
                                ["area_m2"] = UnitHelper.Ft2ToM2(residual),
                                ["materialId"] = null,
                                ["materialName"] = null
                            });
                        }
                    }
                }

                double sideFt2 = parentAreasFt2.Values.Sum();
                double paintedFt2 = regionPaintedFt2.Values.Sum() + parentWholePaintFt2.Values.Sum();

                return new
                {
                    ok = true,
                    elementId = elem.Id.IntValue(),
                    elementClass = elem.GetType().Name,
                    hostKind,
                    side,
                    strategy,
                    totals = new
                    {
                        sideArea_m2 = UnitHelper.Ft2ToM2(sideFt2),
                        paintedArea_m2 = UnitHelper.Ft2ToM2(paintedFt2),
                        unpaintedArea_m2 = UnitHelper.Ft2ToM2(Math.Max(0.0, sideFt2 - paintedFt2))
                    },
                    regions
                };
            }
            catch (Exception ex)
            {
                return Err("get_surface_regions 失敗: " + ex.Message);
            }
        }

        // 親面の抽出（要素種別・モード別に）
        private static bool TryGetParents(
            Document doc, Element elem, string hostKind, string side, JObject p,
            out IList<Reference> parents, out string strategy)
        {
            parents = new List<Reference>();
            strategy = "";

            if (hostKind == "floor" && elem is Autodesk.Revit.DB.Floor fl)
            {
                strategy = "floor:Top/Bottom";
                var tops = new List<Reference>();
                var bottoms = new List<Reference>();
                foreach (var (face, rf) in EnumerateFaces(elem))
                {
                    if (face is PlanarFace pf)
                    {
                        var dot = pf.FaceNormal.Normalize().DotProduct(XYZ.BasisZ);
                        if (dot > 0.5) tops.Add(rf);
                        else if (dot < -0.5) bottoms.Add(rf);
                    }
                }
                if (tops.Count == 0 && bottoms.Count == 0)
                {
                    try
                    {
                        tops = (HostObjectUtils.GetTopFaces(fl) ?? new List<Reference>()).ToList();
                        bottoms = (HostObjectUtils.GetBottomFaces(fl) ?? new List<Reference>()).ToList();
                    }
                    catch { }
                }
                parents = side.Equals("Bottom", StringComparison.OrdinalIgnoreCase) ? (IList<Reference>)bottoms : (IList<Reference>)tops;
                return parents.Count > 0;
            }

            if (hostKind == "roof" && elem is RoofBase rf0)
            {
                strategy = "roof:Top/Bottom";
                var tops = new List<Reference>();
                var bottoms = new List<Reference>();
                foreach (var (face, rf) in EnumerateFaces(elem))
                {
                    if (face is PlanarFace pf)
                    {
                        var dot = pf.FaceNormal.Normalize().DotProduct(XYZ.BasisZ);
                        if (dot > 0.5) tops.Add(rf);
                        else if (dot < -0.5) bottoms.Add(rf);
                    }
                }
                if (tops.Count == 0 && bottoms.Count == 0)
                {
                    try
                    {
                        tops = (HostObjectUtils.GetTopFaces(rf0) ?? new List<Reference>()).ToList();
                        bottoms = (HostObjectUtils.GetBottomFaces(rf0) ?? new List<Reference>()).ToList();
                    }
                    catch { }
                }
                parents = side.Equals("Bottom", StringComparison.OrdinalIgnoreCase) ? (IList<Reference>)bottoms : (IList<Reference>)tops;
                return parents.Count > 0;
            }

            if (hostKind == "ceiling" && elem is HostObject ho)
            {
                strategy = "ceiling:Top/Bottom";
                parents = side.Equals("Top", StringComparison.OrdinalIgnoreCase)
                    ? (HostObjectUtils.GetTopFaces(ho) ?? new List<Reference>())
                    : (HostObjectUtils.GetBottomFaces(ho) ?? new List<Reference>());
                return parents.Count > 0;
            }

            if (hostKind == "wall" && elem is Wall w)
            {
                strategy = "wall:Exterior/Interior";
                var shell = side.Equals("Interior", StringComparison.OrdinalIgnoreCase) ? ShellLayerType.Interior : ShellLayerType.Exterior;
                parents = HostObjectUtils.GetSideFaces(w, shell) ?? new List<Reference>();
                return parents.Count > 0;
            }

            // generic
            // 1) faceIndex 指定 → 親面集合
            var baseList = EnumerateFaces(elem).Select(t => t.rf).ToList();

            var faceIdxArr = p["faceIndex"] as JArray;
            if (faceIdxArr != null && faceIdxArr.Count > 0)
            {
                strategy = "generic:faceIndex[]";
                foreach (var t in faceIdxArr)
                {
                    if (t.Type == JTokenType.Integer)
                    {
                        var idx = t.Value<int>();
                        if (idx >= 0 && idx < baseList.Count) ((List<Reference>)parents).Add(baseList[idx]);
                    }
                }
                return parents.Count > 0;
            }

            // 2) normalFilter
            var nf = p["normalFilter"] as JObject;
            if (nf != null)
            {
                strategy = "generic:normalFilter";
                string axis = nf.Value<string>("axis") ?? "Z";
                double minDot = nf.Value<double?>("minDot") ?? 0.6;
                string sign = nf.Value<string>("sign") ?? "pos"; // pos/neg/any
                var dir = axis == "X" ? XYZ.BasisX : axis == "Y" ? XYZ.BasisY : XYZ.BasisZ;

                foreach (var (face, rf) in EnumerateFaces(elem))
                {
                    if (face is PlanarFace pf)
                    {
                        var d = pf.FaceNormal.Normalize().DotProduct(dir);
                        if (Math.Abs(d) >= minDot && (sign == "any" || (sign == "pos" && d > 0) || (sign == "neg" && d < 0)))
                            ((List<Reference>)parents).Add(rf);
                    }
                }
                return parents.Count > 0;
            }

            // 3) basis + dir（front/back/left/right/up/down）
            var basis = p["basis"] as JObject;
            var dirStr = p.Value<string>("dir");
            if (basis != null && !string.IsNullOrEmpty(dirStr))
            {
                strategy = "generic:basis+dir";
                var fwd = ReadVec(basis["forward"], XYZ.BasisX);
                var upv = ReadVec(basis["up"], XYZ.BasisZ);
                var right = fwd.CrossProduct(upv).Normalize();
                fwd = fwd.Normalize(); upv = upv.Normalize();

                XYZ want =
                    dirStr.Equals("front", StringComparison.OrdinalIgnoreCase) ? fwd :
                    dirStr.Equals("back", StringComparison.OrdinalIgnoreCase) ? (-fwd) :
                    dirStr.Equals("left", StringComparison.OrdinalIgnoreCase) ? (-right) :
                    dirStr.Equals("right", StringComparison.OrdinalIgnoreCase) ? right :
                    dirStr.Equals("down", StringComparison.OrdinalIgnoreCase) ? (-upv) : upv;

                double minDot = p["minDot"]?.Value<double?>() ?? 0.6;

                foreach (var (face, rf) in EnumerateFaces(elem))
                {
                    if (face is PlanarFace pf)
                    {
                        var dot = pf.FaceNormal.Normalize().DotProduct(want);
                        if (dot >= minDot) ((List<Reference>)parents).Add(rf);
                    }
                }
                return parents.Count > 0;
            }

            // 4) 既定：Z上向きフェイス
            strategy = "generic:default-up(Z)";
            foreach (var (face, rf) in EnumerateFaces(elem))
            {
                if (face is PlanarFace pf)
                {
                    var d = pf.FaceNormal.Normalize().DotProduct(XYZ.BasisZ);
                    if (d > 0.6) ((List<Reference>)parents).Add(rf);
                }
            }
            return parents.Count > 0;
        }

        private static IEnumerable<(Face face, Reference rf)> EnumerateFaces(Element elem)
        {
            var res = new List<(Face, Reference)>();
            var opt = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = true, IncludeNonVisibleObjects = false };
            var ge = elem.get_Geometry(opt);
            if (ge == null) return res;

            void Collect(Solid s)
            {
                if (s == null || s.Faces == null || s.Faces.Size == 0) return;
                foreach (Face f in s.Faces)
                {
                    var r = f.Reference;
                    if (r != null) res.Add((f, r));
                }
            }
            foreach (var go in ge)
            {
                if (go is Solid s) Collect(s);
                else if (go is GeometryInstance gi)
                {
                    var g2 = gi.GetInstanceGeometry();
                    if (g2 == null) continue;
                    foreach (var sub in g2) if (sub is Solid ss) Collect(ss);
                }
            }
            return res;
        }

        private static XYZ ReadVec(JToken tok, XYZ fallback)
        {
            try
            {
                if (tok is JObject o)
                    return new XYZ(o.Value<double?>("x") ?? fallback.X, o.Value<double?>("y") ?? fallback.Y, o.Value<double?>("z") ?? fallback.Z);
            }
            catch { }
            return fallback;
        }

        private static object Err(string msg) => new { ok = false, msg };
    }
}


