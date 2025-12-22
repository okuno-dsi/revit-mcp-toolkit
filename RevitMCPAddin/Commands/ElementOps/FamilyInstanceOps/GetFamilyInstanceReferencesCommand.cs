// File: Commands/ElementOps/FamilyInstanceOps/GetFamilyInstanceReferencesCommand.cs
// Purpose: List FamilyInstance reference handles (stable representation) for advanced dimensioning.
// Target : .NET Framework 4.8 / C# 8 / Revit 2023+
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.FamilyInstanceOps
{
    public class GetFamilyInstanceReferencesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_family_instance_references";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = cmd.Params as JObject ?? new JObject();

            bool includeStable = p.Value<bool?>("includeStable") ?? true;
            bool includeGeometry = p.Value<bool?>("includeGeometry") ?? false;
            bool includeEmpty = p.Value<bool?>("includeEmpty") ?? false;
            int maxPerType = Math.Max(0, p.Value<int?>("maxPerType") ?? 50);

            Element target = null;
            int elementId = p.Value<int?>("elementId") ?? 0;
            string uniqueId = p.Value<string>("uniqueId");
            if (elementId > 0) target = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(elementId));
            else if (!string.IsNullOrWhiteSpace(uniqueId)) target = doc.GetElement(uniqueId);
            else
            {
                try
                {
                    var sel = uidoc?.Selection?.GetElementIds();
                    if (sel != null && sel.Count == 1)
                        target = doc.GetElement(sel.First());
                }
                catch { }
            }

            if (target == null)
                return new { ok = false, msg = "elementId/uniqueId を指定するか、要素を1つだけ選択してください。" };

            var fi = target as FamilyInstance;
            if (fi == null)
            {
                return new
                {
                    ok = false,
                    msg = "指定要素は FamilyInstance ではありません。",
                    elementId = target.Id.IntValue(),
                    uniqueId = target.UniqueId,
                    categoryName = target.Category?.Name ?? ""
                };
            }

            HashSet<FamilyInstanceReferenceType> filter = null;
            var filterTok = p["referenceTypes"] as JArray;
            if (filterTok != null && filterTok.Count > 0)
            {
                filter = new HashSet<FamilyInstanceReferenceType>();
                foreach (var t in filterTok)
                {
                    var s = (t?.Value<string>() ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    if (Enum.TryParse(s, true, out FamilyInstanceReferenceType rt))
                        filter.Add(rt);
                }
                if (filter.Count == 0) filter = null;
            }

            var groups = new List<object>();
            foreach (FamilyInstanceReferenceType rt in Enum.GetValues(typeof(FamilyInstanceReferenceType)))
            {
                if (filter != null && !filter.Contains(rt)) continue;

                int count = 0;
                bool truncated = false;
                string error = null;
                var refsOut = new List<object>();

                try
                {
                    var refs = fi.GetReferences(rt);
                    count = refs?.Count ?? 0;

                    if (refs != null && refs.Count > 0)
                    {
                        int take = maxPerType > 0 ? Math.Min(refs.Count, maxPerType) : refs.Count;
                        if (take < refs.Count) truncated = true;

                        for (int i = 0; i < take; i++)
                        {
                            var r = refs[i];
                            string stable = null;
                            if (includeStable)
                            {
                                try { stable = r.ConvertToStableRepresentation(doc); } catch { stable = null; }
                            }

                            object geom = null;
                            if (includeGeometry)
                            {
                                geom = DescribeReferenceGeometry(fi, r);
                            }

                            refsOut.Add(new
                            {
                                index = i,
                                stable,
                                geometry = geom
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                if (includeEmpty || count > 0 || error != null)
                {
                    groups.Add(new
                    {
                        refType = rt.ToString(),
                        count,
                        truncated,
                        error,
                        refs = refsOut
                    });
                }
            }

            var sym = fi.Symbol;
            return new
            {
                ok = true,
                elementId = fi.Id.IntValue(),
                uniqueId = fi.UniqueId,
                categoryId = fi.Category?.Id?.IntValue(),
                categoryName = fi.Category?.Name ?? "",
                familyName = sym?.Family?.Name ?? "",
                typeId = sym?.Id.IntValue(),
                typeName = sym?.Name ?? "",
                includeStable,
                includeGeometry,
                maxPerType,
                referenceTypes = filter?.Select(x => x.ToString()).ToArray(),
                groups
            };
        }

        private static object DescribeReferenceGeometry(FamilyInstance fi, Reference r)
        {
            try
            {
                if (fi == null || r == null) return null;
                var g = fi.GetGeometryObjectFromReference(r);
                if (g == null) return null;

                var face = g as PlanarFace;
                if (face != null)
                {
                    return new
                    {
                        objectType = "PlanarFace",
                        normal = new { x = face.FaceNormal.X, y = face.FaceNormal.Y, z = face.FaceNormal.Z },
                        origin = new { x = face.Origin.X, y = face.Origin.Y, z = face.Origin.Z },
                        areaFt2 = face.Area
                    };
                }

                var edge = g as Edge;
                if (edge != null)
                {
                    try
                    {
                        var c = edge.AsCurve();
                        var p0 = c.GetEndPoint(0);
                        var p1 = c.GetEndPoint(1);
                        return new
                        {
                            objectType = "Edge",
                            curveType = c.GetType().Name,
                            p0 = new { x = p0.X, y = p0.Y, z = p0.Z },
                            p1 = new { x = p1.X, y = p1.Y, z = p1.Z }
                        };
                    }
                    catch
                    {
                        return new { objectType = "Edge" };
                    }
                }

                return new { objectType = g.GetType().Name };
            }
            catch
            {
                return null;
            }
        }
    }
}


