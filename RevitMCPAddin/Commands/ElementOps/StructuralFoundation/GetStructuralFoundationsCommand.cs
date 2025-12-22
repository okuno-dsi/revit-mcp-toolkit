// File: Commands/ElementOps/Foundation/GetStructuralFoundationsCommand.cs (UnitHelper対応)
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Foundation
{
    public class GetStructuralFoundationsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_structural_foundations";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject)cmd.Params;

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            int targetEid = p.Value<int?>("elementId") ?? 0;
            string targetUid = p.Value<string>("uniqueId");

            int filterTypeId = p.Value<int?>("typeId") ?? 0;
            string filterTypeName = p.Value<string>("typeName");
            string filterFamilyName = p.Value<string>("familyName");

            int filterLevelId = p.Value<int?>("levelId") ?? 0;
            string filterLevelName = p.Value<string>("levelName");
            string nameContains = p.Value<string>("nameContains");

            int filterHostId = p.Value<int?>("hostId") ?? 0;
            string filterHostUid = p.Value<string>("hostUniqueId");

            var all = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFoundation)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();

            var typeIds = all.Select(e => e.GetTypeId()?.IntValue() ?? -1).Where(id => id > 0).Distinct().ToList();
            var typeMap = typeIds.ToDictionary(id => id, id => doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id)) as ElementType);

            var levelIds = new HashSet<int>();
            foreach (var e in all) { var lvId = TryGetLevelId(e); if (lvId.HasValue) levelIds.Add(lvId.Value); }
            var levelMap = levelIds.ToDictionary(id => id, id => doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id)) as Level);

            IEnumerable<Element> q = all;
            if (targetEid > 0 || !string.IsNullOrWhiteSpace(targetUid))
            {
                var target = targetEid > 0 ? doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(targetEid)) : doc.GetElement(targetUid);
                q = target == null ? Enumerable.Empty<Element>() : new[] { target };
            }

            if (filterTypeId > 0) q = q.Where(e => (e.GetTypeId()?.IntValue() ?? -1) == filterTypeId);

            if (!string.IsNullOrWhiteSpace(filterTypeName))
            {
                q = q.Where(e =>
                {
                    var tid = e.GetTypeId()?.IntValue() ?? -1;
                    if (!typeMap.TryGetValue(tid, out var et) || et == null) return false;
                    bool ok = string.Equals(et.Name ?? "", filterTypeName, StringComparison.OrdinalIgnoreCase);
                    if (!ok) return false;
                    return string.IsNullOrWhiteSpace(filterFamilyName) || string.Equals(et.FamilyName ?? "", filterFamilyName, StringComparison.OrdinalIgnoreCase);
                });
            }

            if (filterLevelId > 0) q = q.Where(e => (TryGetLevelId(e) ?? -1) == filterLevelId);

            if (!string.IsNullOrWhiteSpace(filterLevelName))
                q = q.Where(e =>
                {
                    var lvId = TryGetLevelId(e);
                    if (!lvId.HasValue) return false;
                    return levelMap.TryGetValue(lvId.Value, out var lv) && lv != null && string.Equals(lv.Name ?? "", filterLevelName, StringComparison.OrdinalIgnoreCase);
                });

            if (!string.IsNullOrWhiteSpace(nameContains))
                q = q.Where(e =>
                {
                    var n = e.Name ?? "";
                    if (n.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    var tid = e.GetTypeId()?.IntValue() ?? -1;
                    var tn = typeMap.TryGetValue(tid, out var et) ? et?.Name ?? "" : "";
                    return tn.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0;
                });

            if (filterHostId > 0 || !string.IsNullOrWhiteSpace(filterHostUid))
            {
                q = q.Where(e =>
                {
                    if (e is FamilyInstance fi && fi.Host != null)
                    {
                        if (filterHostId > 0 && fi.Host.Id.IntValue() != filterHostId) return false;
                        if (!string.IsNullOrWhiteSpace(filterHostUid) && !string.Equals(fi.Host.UniqueId, filterHostUid, StringComparison.OrdinalIgnoreCase)) return false;
                        return true;
                    }
                    if (e is WallFoundation wf)
                    {
                        var wid = wf.WallId;
                        if (wid == null || wid == ElementId.InvalidElementId) return false;
                        if (filterHostId > 0 && wid.IntValue() != filterHostId) return false;
                        if (!string.IsNullOrWhiteSpace(filterHostUid))
                        {
                            var hostWall = doc.GetElement(wid) as Autodesk.Revit.DB.Wall;
                            if (hostWall == null || !string.Equals(hostWall.UniqueId, filterHostUid, StringComparison.OrdinalIgnoreCase)) return false;
                        }
                        return true;
                    }
                    return false;
                });
            }

            var ordered = q.Select(e =>
            {
                var tid = e.GetTypeId()?.IntValue() ?? -1;
                typeMap.TryGetValue(tid, out var et);
                return new { e, tName = et?.Name ?? "" };
            })
            .OrderBy(x => x.tName).ThenBy(x => x.e.Id.IntValue()).Select(x => x.e).ToList();

            int totalCount = ordered.Count;

            if (count == 0)
                return new { ok = true, totalCount, inputUnits = new { Length = "mm" }, internalUnits = new { Length = "ft" } };

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(e =>
                {
                    var n = e.Name ?? "";
                    if (!string.IsNullOrEmpty(n)) return n;
                    var tid = e.GetTypeId()?.IntValue() ?? -1;
                    return typeMap.TryGetValue(tid, out var et) ? et?.Name ?? "" : "";
                }).ToList();

                return new { ok = true, totalCount, names, inputUnits = new { Length = "mm" }, internalUnits = new { Length = "ft" } };
            }

            var page = ordered.Skip(skip).Take(count).ToList();

            var foundations = page.Select(f =>
            {
                var tid = f.GetTypeId()?.IntValue() ?? -1;
                typeMap.TryGetValue(tid, out var et);
                string typeName = et?.Name ?? "";
                string familyName = et?.FamilyName ?? "";

                var lvId = TryGetLevelId(f);
                string levelName = (lvId.HasValue && levelMap.TryGetValue(lvId.Value, out var lv) && lv != null) ? (lv.Name ?? "") : "";

                int? hostId = null; int? hostWallId = null;
                if (f is FamilyInstance fi && fi.Host != null)
                {
                    hostId = fi.Host.Id.IntValue();
                    if (fi.Host is Autodesk.Revit.DB.Wall hw) hostWallId = hw.Id.IntValue();
                }
                if (f is WallFoundation wf)
                {
                    var wid = wf.WallId;
                    if (wid != null && wid != ElementId.InvalidElementId) { hostId = wid.IntValue(); hostWallId = wid.IntValue(); }
                }

                // 位置（内部→mm）: UnitHelper.ConvertDoubleBySpec を使用
                XYZ loc = null; XYZ sPt = null; XYZ ePt = null;
                if (f.Location is LocationPoint lp && lp.Point != null) loc = lp.Point;
                else if (f.Location is LocationCurve lc && lc.Curve != null)
                {
                    sPt = lc.Curve.GetEndPoint(0);
                    ePt = lc.Curve.GetEndPoint(1);
                    loc = (sPt + ePt) / 2.0;
                }
                if (loc == null)
                {
                    var bb = f.get_BoundingBox(null);
                    if (bb != null) loc = (bb.Min + bb.Max) / 2.0;
                }

                object ToMm(XYZ p) => new
                {
                    x = FoundationUnits.ToUser(p.X, SpecTypeId.Length),
                    y = FoundationUnits.ToUser(p.Y, SpecTypeId.Length),
                    z = FoundationUnits.ToUser(p.Z, SpecTypeId.Length)
                };

                return new
                {
                    elementId = f.Id.IntValue(),
                    uniqueId = f.UniqueId,
                    typeId = (tid > 0) ? (int?)tid : null,
                    typeName,
                    familyName,
                    levelId = lvId,
                    levelName,
                    hostId,
                    hostWallId,
                    location = (loc == null) ? null : ToMm(loc),
                    start = (sPt == null) ? null : ToMm(sPt),
                    end = (ePt == null) ? null : ToMm(ePt)
                };
            }).ToList();

            return new { ok = true, totalCount, foundations, inputUnits = new { Length = "mm" }, internalUnits = new { Length = "ft" } };
        }

        // レベルID推定
        private static int? TryGetLevelId(Element e)
        {
            if (e is FamilyInstance fi) return fi.LevelId.IntValue();
            if (e is Autodesk.Revit.DB.Floor fl) return fl.LevelId.IntValue();

            var p = e.get_Parameter(BuiltInParameter.LEVEL_PARAM)
                 ?? e.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM)
                 ?? e.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)
                 ?? e.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);

            if (p != null && p.StorageType == StorageType.ElementId)
            {
                var id = p.AsElementId();
                if (id != null && id != ElementId.InvalidElementId) return id.IntValue();
            }
            return null;
        }
    }
}


