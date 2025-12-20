// File: RevitMCPAddin/Commands/ElementOps/SanitaryFixture/SanitaryFixtureCommands.cs
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Plumbing;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.SanitaryFixture
{
    // ============================================================
    // メタ（入力/内部単位）: UnitHelper に委譲（無ければ固定でもOK）
    // ============================================================
    internal static class UnitsMeta
    {
        public static object InputUnits() => UnitHelper.InputUnitsMeta();     // 例: { Length="mm", Area="mm2", Volume="mm3", Angle="deg" }
        public static object InternalUnits() => UnitHelper.InternalUnitsMeta(); // 例: { Length="ft", Area="ft2", Volume="ft3", Angle="rad" }
    }

    // ============================================================
    // 1) 一覧取得 get_sanitary_fixtures（filters/paging/namesOnly）
    // ============================================================
    public class GetSanitaryFixturesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_sanitary_fixtures";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject)cmd.Params;

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            // フィルタ
            int filterTypeId = p.Value<int?>("typeId") ?? 0;
            string filterTypeName = p.Value<string>("typeName");
            string filterFamilyName = p.Value<string>("familyName");
            int filterLevelId = p.Value<int?>("levelId") ?? 0;
            string filterLevelName = p.Value<string>("levelName");
            string nameContains = p.Value<string>("nameContains");

            var all = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .ToList();

            // キャッシュ
            var typeIds = all.Select(x => x.GetTypeId().IntegerValue).Distinct().ToList();
            var typeMap = new Dictionary<int, FamilySymbol>(typeIds.Count);
            foreach (var id in typeIds) typeMap[id] = doc.GetElement(new ElementId(id)) as FamilySymbol;

            var levelIds = all.Select(x => x.LevelId.IntegerValue).Distinct().ToList();
            var levelMap = new Dictionary<int, Level>(levelIds.Count);
            foreach (var id in levelIds) levelMap[id] = doc.GetElement(new ElementId(id)) as Level;

            IEnumerable<FamilyInstance> q = all;

            // typeId / typeName(+familyName)
            if (filterTypeId > 0)
                q = q.Where(x => x.GetTypeId().IntegerValue == filterTypeId);

            if (!string.IsNullOrWhiteSpace(filterTypeName))
            {
                q = q.Where(x =>
                {
                    typeMap.TryGetValue(x.GetTypeId().IntegerValue, out var s);
                    if (s == null) return false;
                    bool ok = string.Equals(s.Name ?? "", filterTypeName, StringComparison.OrdinalIgnoreCase);
                    if (!ok) return false;
                    if (!string.IsNullOrWhiteSpace(filterFamilyName))
                        return string.Equals(s.Family?.Name ?? "", filterFamilyName, StringComparison.OrdinalIgnoreCase);
                    return true;
                });
            }

            // level
            if (filterLevelId > 0)
                q = q.Where(x => x.LevelId.IntegerValue == filterLevelId);

            if (!string.IsNullOrWhiteSpace(filterLevelName))
            {
                q = q.Where(x =>
                {
                    levelMap.TryGetValue(x.LevelId.IntegerValue, out var lv);
                    return lv != null && string.Equals(lv.Name ?? "", filterLevelName, StringComparison.OrdinalIgnoreCase);
                });
            }

            // nameContains（インスタンス名 or タイプ名）
            if (!string.IsNullOrWhiteSpace(nameContains))
            {
                q = q.Where(x =>
                {
                    var n = x.Name ?? "";
                    if (n.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    typeMap.TryGetValue(x.GetTypeId().IntegerValue, out var s);
                    return (s?.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0;
                });
            }

            // 並び: typeName -> elementId
            var ordered = q.Select(x =>
            {
                typeMap.TryGetValue(x.GetTypeId().IntegerValue, out var s);
                string tName = s?.Name ?? "";
                return new { x, tName };
            })
            .OrderBy(a => a.tName)
            .ThenBy(a => a.x.Id.IntegerValue)
            .Select(a => a.x)
            .ToList();

            int totalCount = ordered.Count;

            // メタのみ
            if (count == 0)
                return ResultUtil.Ok(new { totalCount, inputUnits = UnitsMeta.InputUnits(), internalUnits = UnitsMeta.InternalUnits() });

            // namesOnly
            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(x =>
                {
                    var n = x.Name ?? "";
                    if (!string.IsNullOrEmpty(n)) return n;
                    typeMap.TryGetValue(x.GetTypeId().IntegerValue, out var s);
                    return s?.Name ?? "";
                }).ToList();

                return ResultUtil.Ok(new { totalCount, names, inputUnits = UnitsMeta.InputUnits(), internalUnits = UnitsMeta.InternalUnits() });
            }

            var fixtures = ordered.Skip(skip).Take(count).Select(fi =>
            {
                // 位置
                XYZ pt = null;
                if (fi.Location is LocationPoint lp && lp.Point != null) pt = lp.Point;

                typeMap.TryGetValue(fi.GetTypeId().IntegerValue, out var sym);
                string typeName = sym?.Name ?? "";
                string familyName = sym?.Family?.Name ?? "";

                levelMap.TryGetValue(fi.LevelId.IntegerValue, out var lv);
                string levelName = lv?.Name ?? "";

                return new
                {
                    elementId = fi.Id.IntegerValue,
                    uniqueId = fi.UniqueId,
                    typeId = fi.GetTypeId().IntegerValue,
                    typeName,
                    familyName,
                    levelId = fi.LevelId.IntegerValue,
                    levelName,
                    location = UnitHelper.XyzToMm(pt) // ★ UnitHelper へ統一
                };
            }).ToList();

            return ResultUtil.Ok(new
            {
                totalCount,
                fixtures,
                inputUnits = UnitsMeta.InputUnits(),
                internalUnits = UnitsMeta.InternalUnits()
            });
        }
    }

    // ============================================================
    // 2) パラメータ一括更新 update_sanitary_fixture（mm/deg入力→内部変換）
    // ============================================================
    public class UpdateSanitaryFixtureCommand : IRevitCommandHandler
    {
        public string CommandName => "update_sanitary_fixture";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject)cmd.Params;

            Element el = null;
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) el = doc.GetElement(new ElementId(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) el = doc.GetElement(uid);
            if (el == null) return ResultUtil.Err("要素が見つかりません（elementId/uniqueId）。");

            var parmObj = p["parameters"] as JObject;
            if (parmObj == null) return ResultUtil.Err("parameters が必要です。");

            using (var tx = new Transaction(doc, "Update Sanitary Fixture Params"))
            {
                tx.Start();
                foreach (var prop in parmObj.Properties())
                {
                    var prm = el.LookupParameter(prop.Name);
                    if (prm == null) { tx.RollBack(); return ResultUtil.Err($"Parameter '{prop.Name}' not found"); }
                    if (prm.IsReadOnly) { tx.RollBack(); return ResultUtil.Err($"Parameter '{prop.Name}' is read-only"); }

                    try
                    {
                        switch (prm.StorageType)
                        {
                            case StorageType.Double:
                                {
                                    // mm/deg 入力 → Spec に応じて内部へ（UnitHelperに委譲）
                                    var spec = UnitHelper.GetSpec(prm); // prm.Definition.GetDataType() を内包して旧環境も吸収する想定
                                    double vUser = prop.Value.Value<double>();
                                    double vInternal = UnitHelper.ToInternalBySpec(vUser, spec);
                                    prm.Set(vInternal);
                                    break;
                                }
                            case StorageType.Integer:
                                prm.Set(prop.Value.Value<int>());
                                break;
                            case StorageType.String:
                                prm.Set(prop.Value.Type == JTokenType.Null ? string.Empty : prop.Value.Value<string>());
                                break;
                            case StorageType.ElementId:
                                prm.Set(new ElementId(prop.Value.Value<int>()));
                                break;
                            default:
                                // 未対応データ型はスキップ or 失敗にするかは方針次第
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();
                        return ResultUtil.Err($"Failed to set '{prop.Name}': {ex.Message}");
                    }
                }
                tx.Commit();
            }

            return ResultUtil.Ok(new { elementId = el.Id.IntegerValue, uniqueId = el.UniqueId });
        }
    }

    // ============================================================
    // 3) 移動 move_sanitary_fixture（offset または dx/dy/dz）: mm→ft は UnitHelper
    // ============================================================
    public class MoveSanitaryFixtureCommand : IRevitCommandHandler
    {
        public string CommandName => "move_sanitary_fixture";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject)cmd.Params;

            Element el = null;
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) el = doc.GetElement(new ElementId(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) el = doc.GetElement(uid);
            if (el == null) return ResultUtil.Err("要素が見つかりません（elementId/uniqueId）。");

            XYZ offset;
            var off = p.Value<JObject>("offset");
            if (off != null)
            {
                offset = new XYZ(
                    UnitHelper.MmToInternalLength(off.Value<double>("x")),
                    UnitHelper.MmToInternalLength(off.Value<double>("y")),
                    UnitHelper.MmToInternalLength(off.Value<double>("z")));
            }
            else
            {
                double dx = p.Value<double?>("dx") ?? 0;
                double dy = p.Value<double?>("dy") ?? 0;
                double dz = p.Value<double?>("dz") ?? 0;
                offset = new XYZ(
                    UnitHelper.MmToInternalLength(dx),
                    UnitHelper.MmToInternalLength(dy),
                    UnitHelper.MmToInternalLength(dz));
            }

            using (var tx = new Transaction(doc, "Move Sanitary Fixture"))
            {
                tx.Start();
                ElementTransformUtils.MoveElement(doc, el.Id, offset);
                tx.Commit();
            }

            return ResultUtil.Ok(new
            {
                elementId = el.Id.IntegerValue,
                uniqueId = el.UniqueId,
                inputUnits = UnitsMeta.InputUnits(),
                internalUnits = UnitsMeta.InternalUnits()
            });
        }
    }

    // ============================================================
    // 4) 削除 delete_sanitary_fixture（elementId/uniqueId）
    // ============================================================
    public class DeleteSanitaryFixtureCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_sanitary_fixture";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject)cmd.Params;

            Element el = null;
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) el = doc.GetElement(new ElementId(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) el = doc.GetElement(uid);
            if (el == null) return ResultUtil.Err("要素が見つかりません（elementId/uniqueId）。");

            ICollection<ElementId> deleted = null;
            using (var tx = new Transaction(doc, "Delete Sanitary Fixture"))
            {
                tx.Start();
                deleted = doc.Delete(el.Id);
                tx.Commit();
            }

            return ResultUtil.Ok(new { deletedCount = deleted?.Count ?? 0 });
        }
    }

    // ============================================================
    // 5) タイプ一覧 get_sanitary_fixture_types（paging/namesOnly/部分一致）
    // ============================================================
    public class GetSanitaryFixtureTypesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_sanitary_fixture_types";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject)cmd.Params;

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;
            string typeName = p.Value<string>("typeName");
            string familyName = p.Value<string>("familyName");
            string nameContains = p.Value<string>("nameContains");

            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                .Cast<FamilySymbol>()
                .ToList();

            IEnumerable<FamilySymbol> q = all;

            if (!string.IsNullOrWhiteSpace(typeName))
            {
                q = q.Where(s => string.Equals(s.Name ?? "", typeName, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(familyName))
                    q = q.Where(s => string.Equals(s.Family?.Name ?? "", familyName, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(familyName))
                q = q.Where(s => string.Equals(s.Family?.Name ?? "", familyName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(nameContains))
                q = q.Where(s => (s.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);

            var ordered = q.Select(s => new { s, fam = s.Family != null ? (s.Family.Name ?? "") : "", name = s.Name ?? "", id = s.Id.IntegerValue })
                           .OrderBy(x => x.fam).ThenBy(x => x.name).ThenBy(x => x.id)
                           .Select(x => x.s).ToList();

            int totalCount = ordered.Count;

            if (count == 0)
                return ResultUtil.Ok(new { totalCount, inputUnits = UnitsMeta.InputUnits(), internalUnits = UnitsMeta.InternalUnits() });

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(s => s.Name ?? "").ToList();
                return ResultUtil.Ok(new { totalCount, names, inputUnits = UnitsMeta.InputUnits(), internalUnits = UnitsMeta.InternalUnits() });
            }

            var types = ordered.Skip(skip).Take(count).Select(s => new
            {
                typeId = s.Id.IntegerValue,
                uniqueId = s.UniqueId,
                typeName = s.Name ?? "",
                familyId = s.Family != null ? s.Family.Id.IntegerValue : (int?)null,
                familyName = s.Family != null ? (s.Family.Name ?? "") : ""
            }).ToList();

            return ResultUtil.Ok(new { totalCount, types, inputUnits = UnitsMeta.InputUnits(), internalUnits = UnitsMeta.InternalUnits() });
        }
    }

    // ============================================================
    // 6) タイプ複製 duplicate_sanitary_fixture_type
    // ============================================================
    public class DuplicateSanitaryFixtureTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "duplicate_sanitary_fixture_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject)cmd.Params;

            var srcId = new ElementId(p.Value<int>("sourceTypeId"));
            var sym = doc.GetElement(srcId) as FamilySymbol;
            if (sym == null) return ResultUtil.Err($"Type not found: {srcId.IntegerValue}");

            string newName = p.Value<string>("newTypeName");
            if (string.IsNullOrWhiteSpace(newName)) return ResultUtil.Err("newTypeName が必要です。");

            FamilySymbol newSym = null;
            using (var tx = new Transaction(doc, "Duplicate Sanitary Fixture Type"))
            {
                tx.Start();
                newSym = sym.Duplicate(newName) as FamilySymbol;
                tx.Commit();
            }
            if (newSym == null) return ResultUtil.Err("タイプの複製に失敗しました。");

            return ResultUtil.Ok(new { newTypeId = newSym.Id.IntegerValue, newTypeName = newSym.Name, uniqueId = newSym.UniqueId });
        }
    }

    // ============================================================
    // 7) タイプ削除 delete_sanitary_fixture_type
    // ============================================================
    public class DeleteSanitaryFixtureTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_sanitary_fixture_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject)cmd.Params;

            int tid = p.Value<int>("typeId");
            var id = new ElementId(tid);
            var t = doc.GetElement(id) as FamilySymbol;
            if (t == null) return ResultUtil.Err($"Type not found: {tid}");

            ICollection<ElementId> deleted = null;
            using (var tx = new Transaction(doc, "Delete Sanitary Fixture Type"))
            {
                tx.Start();
                deleted = doc.Delete(id);
                tx.Commit();
            }

            return ResultUtil.Ok(new { deletedCount = deleted?.Count ?? 0 });
        }
    }

    // ============================================================
    // 8) インスタンスのタイプ変更 change_sanitary_fixture_type
    //    newTypeId もしくは typeName(+familyName) で変更
    // ============================================================
    public class ChangeSanitaryFixtureTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "change_sanitary_fixture_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject)cmd.Params;

            Element el = null;
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) el = doc.GetElement(new ElementId(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) el = doc.GetElement(uid);
            var inst = el as FamilyInstance;
            if (inst == null) return ResultUtil.Err("FamilyInstance が見つかりません（elementId/uniqueId）。");

            FamilySymbol newSym = null;
            int newTypeId = p.Value<int?>("newTypeId") ?? 0;
            if (newTypeId > 0)
            {
                newSym = doc.GetElement(new ElementId(newTypeId)) as FamilySymbol;
            }
            else
            {
                string typeName = p.Value<string>("typeName");
                string familyName = p.Value<string>("familyName");
                if (!string.IsNullOrWhiteSpace(typeName))
                {
                    var q = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                        .Cast<FamilySymbol>()
                        .Where(s => string.Equals(s.Name ?? "", typeName, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(familyName))
                        q = q.Where(s => string.Equals(s.Family?.Name ?? "", familyName, StringComparison.OrdinalIgnoreCase));
                    newSym = q.OrderBy(s => s.Family?.Name ?? "").ThenBy(s => s.Name ?? "").FirstOrDefault();
                }
            }
            if (newSym == null) return ResultUtil.Err("新しいタイプが見つかりません。");

            using (var tx = new Transaction(doc, "Change Sanitary Fixture Type"))
            {
                tx.Start();
                inst.ChangeTypeId(newSym.Id);
                tx.Commit();
            }

            return ResultUtil.Ok(new { elementId = inst.Id.IntegerValue, uniqueId = inst.UniqueId, typeId = inst.GetTypeId().IntegerValue });
        }
    }
}
