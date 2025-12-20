// File: RevitMCPAddin/Commands/ElementOps/CurtainWall/GetCurtainWallsCommand.cs
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.Commands.ElementOps.CurtainWall;
using static Autodesk.Revit.DB.UnitUtils;

namespace RevitMCPAddin.Commands.ElementOps.CurtainWall
{
    /// <summary>
    /// カーテンウォール一覧（filters/paging/namesOnly）
    /// filters: elementId/uniqueId, typeId/typeName, levelId/levelName, nameContains
    /// 返却: type/family/level/位置(BB中心mm)/グリッド有無/長さmm
    /// </summary>
    public class GetCurtainWallsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_curtain_walls";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)(cmd.Params ?? new JObject());

            // 既存ページング（互換）
            int legacySkip = p.Value<int?>("skip") ?? 0;
            int legacyCount = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            // shape/paging + 軽量出力
            var shape = p["_shape"] as JObject;
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
            var page = shape?["page"] as JObject;
            int limit = Math.Max(0, page?.Value<int?>("limit") ?? legacyCount);
            int skip = Math.Max(0, page?.Value<int?>("skip") ?? page?.Value<int?>("offset") ?? legacySkip);
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;
            bool includeLocation = p.Value<bool?>("includeLocation") ?? true;

            // single target
            int targetEid = p.Value<int?>("elementId") ?? 0;
            string targetUid = p.Value<string>("uniqueId");

            // filters
            int typeId = p.Value<int?>("typeId") ?? 0;
            string typeName = p.Value<string>("typeName");
            int levelId = p.Value<int?>("levelId") ?? 0;
            string levelName = p.Value<string>("levelName");
            string nameContains = p.Value<string>("nameContains");

            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(Autodesk.Revit.DB.Wall))
                .Cast<Autodesk.Revit.DB.Wall>()
                .Where(w => w.CurtainGrid != null);

            IEnumerable<Autodesk.Revit.DB.Wall> q = collector;

            // single target
            if (targetEid > 0 || !string.IsNullOrWhiteSpace(targetUid))
            {
                Autodesk.Revit.DB.Wall target = null;
                if (targetEid > 0) target = doc.GetElement(new ElementId(targetEid)) as Autodesk.Revit.DB.Wall;
                else target = doc.GetElement(targetUid) as Autodesk.Revit.DB.Wall;
                q = (target != null && target.CurtainGrid != null) ? new[] { target } : Enumerable.Empty<Autodesk.Revit.DB.Wall>();
            }

            if (typeId > 0)
                q = q.Where(w => w.GetTypeId().IntegerValue == typeId);

            if (!string.IsNullOrWhiteSpace(typeName))
                q = q.Where(w => string.Equals((doc.GetElement(w.GetTypeId()) as ElementType)?.Name ?? "", typeName, StringComparison.OrdinalIgnoreCase));

            if (levelId > 0)
                q = q.Where(w => w.LevelId.IntegerValue == levelId);

            if (!string.IsNullOrWhiteSpace(levelName))
                q = q.Where(w =>
                {
                    var lv = doc.GetElement(w.LevelId) as Level;
                    return lv != null && string.Equals(lv.Name ?? "", levelName, StringComparison.OrdinalIgnoreCase);
                });

            if (!string.IsNullOrWhiteSpace(nameContains))
                q = q.Where(w => (w.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0
                              || ((doc.GetElement(w.GetTypeId()) as ElementType)?.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);

            // Materialize + 型名を事前キャッシュ
            var filtered = q.ToList();
            var typeNameMap = new Dictionary<int, string>();
            var familyNameMap = new Dictionary<int, string>();
            foreach (var tid in filtered.Select(w => w.GetTypeId().IntegerValue).Distinct())
            {
                var et = doc.GetElement(new ElementId(tid)) as ElementType;
                typeNameMap[tid] = et?.Name ?? string.Empty;
                familyNameMap[tid] = (et as WallType)?.FamilyName ?? string.Empty;
            }

            var ordered = filtered
                .OrderBy(w => typeNameMap.TryGetValue(w.GetTypeId().IntegerValue, out var n) ? n : string.Empty)
                .ThenBy(w => w.Id.IntegerValue)
                .ToList();

            int totalCount = ordered.Count;

            if (summaryOnly || limit == 0)
                return new { ok = true, totalCount, inputUnits = CurtainUtil.UnitsIn(), internalUnits = CurtainUtil.UnitsInt() };

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(limit).Select(w =>
                {
                    var n = w.Name ?? "";
                    if (!string.IsNullOrEmpty(n)) return n;
                    var tid2 = w.GetTypeId().IntegerValue;
                    return typeNameMap.TryGetValue(tid2, out var n2) ? n2 : string.Empty;
                }).ToList();

                return new { ok = true, totalCount, names, inputUnits = CurtainUtil.UnitsIn(), internalUnits = CurtainUtil.UnitsInt() };
            }

            // Paging + 軽量 idsOnly
            IEnumerable<Autodesk.Revit.DB.Wall> paged = ordered;
            if (skip > 0) paged = paged.Skip(skip);
            if (limit > 0 && limit != int.MaxValue) paged = paged.Take(limit);

            if (idsOnly)
            {
                var ids = paged.Select(w => w.Id.IntegerValue).ToList();
                return new { ok = true, totalCount, elementIds = ids, inputUnits = CurtainUtil.UnitsIn(), internalUnits = CurtainUtil.UnitsInt() };
            }

            var list = paged.Select(w =>
            {
                // 位置：BB中心 & 長さ
                XYZ center = null;
                double lengthMm = 0.0;
                try
                {
                    if (includeLocation)
                    {
                        var lc = w.Location as LocationCurve;
                        if (lc != null && lc.Curve != null)
                        {
                            lengthMm = Math.Round(CurtainUtil.FtToMm(lc.Curve.Length), 3);
                            var bb = w.get_BoundingBox(null);
                            if (bb != null) center = (bb.Min + bb.Max) / 2.0;
                        }
                    }
                }
                catch { }

                object location = center == null ? null : new
                {
                    x = Math.Round(CurtainUtil.FtToMm(center.X), 3),
                    y = Math.Round(CurtainUtil.FtToMm(center.Y), 3),
                    z = Math.Round(CurtainUtil.FtToMm(center.Z), 3),
                };

                var tid = w.GetTypeId().IntegerValue;
                var wt = doc.GetElement(new ElementId(tid)) as WallType;
                var lv = doc.GetElement(w.LevelId) as Level;

                // 高さ（Unconnected Height）mm（接続時は0の可能性）
                double heightFt = CurtainUtil.TryGetParamDouble(w, BuiltInParameter.WALL_USER_HEIGHT_PARAM, 0);
                double heightMm = Math.Round(CurtainUtil.FtToMm(heightFt), 3);

                return new
                {
                    elementId = w.Id.IntegerValue,
                    uniqueId = w.UniqueId,
                    typeId = tid,
                    typeName = (typeNameMap.TryGetValue(tid, out var tnm) ? tnm : wt?.Name) ?? "",
                    familyName = (familyNameMap.TryGetValue(tid, out var fnm) ? fnm : wt?.FamilyName) ?? "",
                    levelId = w.LevelId.IntegerValue,
                    levelName = lv?.Name ?? "",
                    lengthMm,
                    heightMm,
                    hasCurtainGrid = (w.CurtainGrid != null),
                    location
                };
            }).ToList();

            return new { ok = true, totalCount, curtainWalls = list, inputUnits = CurtainUtil.UnitsIn(), internalUnits = CurtainUtil.UnitsInt() };
        }
    }
}
