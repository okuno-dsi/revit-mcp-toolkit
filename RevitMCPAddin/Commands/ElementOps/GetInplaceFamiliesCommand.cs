// ================================================================
// File: Commands/ElementOps/GetInplaceFamiliesCommand.cs (UnitHelper対応・完全版)
// 概要: モデル内のインプレースファミリ一覧を取得（読み取り専用）
// 仕様: elementId/uniqueId ターゲット、family/type/category/level フィルタ、
//       ページング（skip/count; count=0 はメタのみ）、namesOnly=true、
//       返却一貫性（elementId/typeId/uniqueId）＋単位メタ、安定ソート、null安全
// 単位: 位置座標は UnitHelper で mm 正規化、メタに inputUnits/internalUnits を付与
// 対応: Revit 2023+ / .NET Framework 4.8
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core; // UnitHelper, ResultUtil 等

namespace RevitMCPAddin.Commands.ElementOps
{
    public class GetInplaceFamiliesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_inplace_families";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
                return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());

            // -----------------------------
            // 入力
            // -----------------------------
            int legacySkip = p.Value<int?>("skip") ?? 0;
            int legacyCount = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;
            // shape/paging
            var shape = p["_shape"] as JObject;
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
            var pageObj = shape?["page"] as JObject;
            int limit = Math.Max(0, pageObj?.Value<int?>("limit") ?? legacyCount);
            int skip = Math.Max(0, pageObj?.Value<int?>("skip") ?? pageObj?.Value<int?>("offset") ?? legacySkip);
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;

            // 単一ターゲット（優先）
            int targetEid = p.Value<int?>("elementId") ?? 0;
            string targetUid = p.Value<string>("uniqueId");

            // フィルタ
            string filterFamilyName = p.Value<string>("familyName");
            string filterTypeName = p.Value<string>("typeName");
            string filterCategory = p.Value<string>("category");   // カテゴリ名
            int filterLevelId = p.Value<int?>("levelId") ?? 0;
            string filterLevelName = p.Value<string>("levelName");
            string nameContains = p.Value<string>("nameContains"); // family/type の部分一致

            // -----------------------------
            // ベース集合: In-Place の FamilyInstance のみ
            // -----------------------------
            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(fi => fi?.Symbol?.Family != null && fi.Symbol.Family.IsInPlace)
                .ToList();

            // キャッシュ: type / level
            var typeIds = all.Select(x => x.GetTypeId().IntegerValue).Distinct().ToList();
            var typeMap = new Dictionary<int, FamilySymbol>(typeIds.Count);
            foreach (var id in typeIds)
                typeMap[id] = doc.GetElement(new ElementId(id)) as FamilySymbol;

            var levelIds = all.Select(x => x.LevelId.IntegerValue).Distinct().ToList();
            var levelMap = new Dictionary<int, Level>(levelIds.Count);
            foreach (var id in levelIds)
                levelMap[id] = doc.GetElement(new ElementId(id)) as Level;

            IEnumerable<FamilyInstance> q = all;

            // -----------------------------
            // 単一ターゲット優先
            // -----------------------------
            if (targetEid > 0 || !string.IsNullOrWhiteSpace(targetUid))
            {
                FamilyInstance target = null;
                if (targetEid > 0) target = doc.GetElement(new ElementId(targetEid)) as FamilyInstance;
                else target = doc.GetElement(targetUid) as FamilyInstance;

                // In-Place でなければ除外
                if (target == null || target.Symbol?.Family == null || !target.Symbol.Family.IsInPlace)
                    q = Enumerable.Empty<FamilyInstance>();
                else
                    q = new[] { target };
            }

            // -----------------------------
            // フィルタ適用
            // -----------------------------
            if (!string.IsNullOrWhiteSpace(filterFamilyName))
                q = q.Where(fi => string.Equals(fi.Symbol?.Family?.Name ?? "", filterFamilyName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(filterTypeName))
                q = q.Where(fi => string.Equals(fi.Symbol?.Name ?? "", filterTypeName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(filterCategory))
                q = q.Where(fi => string.Equals(fi.Category?.Name ?? "", filterCategory, StringComparison.OrdinalIgnoreCase));

            if (filterLevelId > 0)
                q = q.Where(fi => fi.LevelId.IntegerValue == filterLevelId);

            if (!string.IsNullOrWhiteSpace(filterLevelName))
                q = q.Where(fi =>
                {
                    levelMap.TryGetValue(fi.LevelId.IntegerValue, out var lv);
                    return lv != null && string.Equals(lv.Name ?? "", filterLevelName, StringComparison.OrdinalIgnoreCase);
                });

            if (!string.IsNullOrWhiteSpace(nameContains))
            {
                q = q.Where(fi =>
                {
                    var fn = fi.Symbol?.Family?.Name ?? "";
                    var tn = fi.Symbol?.Name ?? "";
                    return (fn.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                        || (tn.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);
                });
            }

            // -----------------------------
            // 並び: familyName -> typeName -> elementId
            // -----------------------------
            var ordered = q
                .Select(fi => new
                {
                    fi,
                    fam = fi.Symbol?.Family?.Name ?? "",
                    typ = fi.Symbol?.Name ?? "",
                    id = fi.Id.IntegerValue
                })
                .OrderBy(x => x.fam)
                .ThenBy(x => x.typ)
                .ThenBy(x => x.id)
                .Select(x => x.fi)
                .ToList();

            int totalCount = ordered.Count;

            // -----------------------------
            // メタのみ（count=0） → ここで確実に return
            // -----------------------------
            if (summaryOnly || limit == 0)
            {
                return new
                {
                    ok = true,
                    totalCount,
                    inputUnits = UnitHelper.InputUnitsMeta(),
                    internalUnits = UnitHelper.InternalUnitsMeta()
                };
            }

            // -----------------------------
            // namesOnly → ここで確実に return
            // -----------------------------
            if (namesOnly)
            {
                var names = ordered
                    .Skip(skip)
                    .Take(limit)
                    .Select(fi =>
                    {
                        var fam = fi.Symbol?.Family?.Name ?? "";
                        var typ = fi.Symbol?.Name ?? "";
                        return string.IsNullOrEmpty(typ) ? fam : $"{fam} : {typ}";
                    })
                    .ToList();

                return new
                {
                    ok = true,
                    totalCount,
                    names,
                    inputUnits = UnitHelper.InputUnitsMeta(),
                    internalUnits = UnitHelper.InternalUnitsMeta()
                };
            }

            // -----------------------------
            // フル項目 → 最後に return
            // -----------------------------
            IEnumerable<FamilyInstance> seq = ordered;
            if (skip > 0) seq = seq.Skip(skip);
            if (limit > 0 && limit != int.MaxValue) seq = seq.Take(limit);

            if (idsOnly)
            {
                var ids = seq.Select(fi => fi.Id.IntegerValue).ToList();
                return new { ok = true, totalCount, elementIds = ids, inputUnits = UnitHelper.InputUnitsMeta(), internalUnits = UnitHelper.InternalUnitsMeta() };
            }

            var page = seq.ToList();

            var inplaceFamilies = page.Select(fi =>
            {
                // level
                levelMap.TryGetValue(fi.LevelId.IntegerValue, out var lv);
                string levelName = lv?.Name ?? "";

                // category
                string categoryName = fi.Category?.Name ?? "";

                // type/family
                typeMap.TryGetValue(fi.GetTypeId().IntegerValue, out var sym);
                string familyName = sym?.Family?.Name ?? "";
                string typeName = sym?.Name ?? "";

                // 位置: LocationPoint / LocationCurve / BoundingBox 中心
                XYZ loc = null; XYZ sPt = null, ePt = null;
                var lp = fi.Location as LocationPoint;
                if (lp?.Point != null)
                {
                    loc = lp.Point;
                }
                else
                {
                    var lc = fi.Location as LocationCurve;
                    if (lc?.Curve != null)
                    {
                        sPt = lc.Curve.GetEndPoint(0);
                        ePt = lc.Curve.GetEndPoint(1);
                        loc = (sPt + ePt) / 2.0;
                    }
                }
                if (loc == null)
                {
                    var bb = fi.get_BoundingBox(null);
                    if (bb != null) loc = (bb.Min + bb.Max) / 2.0;
                }

                object location = null;
                if (loc != null)
                {
                    location = new
                    {
                        x = Math.Round(UnitHelper.FtToMm(loc.X), 3),
                        y = Math.Round(UnitHelper.FtToMm(loc.Y), 3),
                        z = Math.Round(UnitHelper.FtToMm(loc.Z), 3),
                    };
                }

                return new
                {
                    elementId = fi.Id.IntegerValue,
                    uniqueId = fi.UniqueId,
                    typeId = fi.GetTypeId().IntegerValue,
                    familyName,
                    typeName,
                    category = categoryName,
                    levelId = fi.LevelId.IntegerValue,
                    levelName,
                    location
                };
            }).ToList();

            return new
            {
                ok = true,
                totalCount,
                inplaceFamilies,
                inputUnits = UnitHelper.InputUnitsMeta(),
                internalUnits = UnitHelper.InternalUnitsMeta()
            };
        }
    }
}
