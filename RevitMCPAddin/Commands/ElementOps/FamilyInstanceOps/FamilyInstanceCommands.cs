// ================================================================
// File: Commands/ElementOps/FamilyInstanceOps/FamilyInstanceCommands.cs (UnitHelper対応版)
// 目的: 長さ/角度/面積/体積の入出力と Parameter(Double)の変換を UnitHelper に一元化
// 対象: Revit 2023 / .NET Framework 4.8 / C# 8
// 依存: RevitMCPAddin.Core.UnitHelper
// 変更: 旧FamUtilの ToMm/MmToFt/ConvertDoubleBySpec/ToInternalBySpec を UnitHelper に委譲
// ================================================================
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.FamilyInstanceOps
{
    internal static class FamUtil
    {
        // ---- 単位表示メタ（既存表示は維持）----
        public static object UnitsIn() => new { Length = "mm", Area = "mm2", Volume = "mm3", Angle = "deg" };
        public static object UnitsInt() => new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" };

        // ---- 変換: UnitHelper 委譲（安全フォールバック付き）----
        public static double ToMm(double ft)
        {
            try { return UnitHelper.FtToMm(ft); }
            catch { return UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters); }
        }
        public static double MmToFt(double mm)
        {
            try { return UnitHelper.MmToFt(mm); }
            catch { return UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters); }
        }

        /// <summary>
        /// Parameter(Double) の取得時: ForgeTypeId(Spec) に従ってユーザー向け数値へ
        /// </summary>
        public static object ConvertDoubleBySpec(double raw, ForgeTypeId fdt)
        {
            try { return UnitHelper.ConvertDoubleBySpec(raw, fdt); }
            catch
            {
                // フォールバック（Length/Area/Volume/Angle 代表）
                try
                {
                    if (fdt != null)
                    {
                        if (fdt.Equals(SpecTypeId.Length)) return Math.Round(UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.Millimeters), 3);
                        if (fdt.Equals(SpecTypeId.Area)) return Math.Round(UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.SquareMillimeters), 3);
                        if (fdt.Equals(SpecTypeId.Volume)) return Math.Round(UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.CubicMillimeters), 3);
                        if (fdt.Equals(SpecTypeId.Angle)) return Math.Round(UnitHelper.InternalToDeg(raw), 3);
                    }
                }
                catch { }
                return Math.Round(raw, 3);
            }
        }

        /// <summary>
        /// Parameter(Double) の設定時: ForgeTypeId(Spec) に従って内部値へ
        /// </summary>
        public static double ToInternalBySpec(double user, ForgeTypeId fdt)
        {
            try { return UnitHelper.ToInternalBySpec(user, fdt); }
            catch
            {
                // フォールバック
                try
                {
                    if (fdt != null)
                    {
                        if (fdt.Equals(SpecTypeId.Length)) return UnitUtils.ConvertToInternalUnits(user, UnitTypeId.Millimeters);
                        if (fdt.Equals(SpecTypeId.Area)) return UnitUtils.ConvertToInternalUnits(user, UnitTypeId.SquareMillimeters);
                        if (fdt.Equals(SpecTypeId.Volume)) return UnitUtils.ConvertToInternalUnits(user, UnitTypeId.CubicMillimeters);
                        if (fdt.Equals(SpecTypeId.Angle)) return UnitHelper.DegToInternal(user);
                    }
                }
                catch { }
                return user;
            }
        }

        // ---- 角度ユーティリティ（UnitHelper が無い場合でも安全に使える）----
        internal static double RadToDegSafe(double rad) => rad * (180.0 / Math.PI);
        internal static double DegToRadSafe(double deg) => deg * (Math.PI / 180.0);

        public static bool IsLoadableFamilyInstance(Element e)
        {
            var fi = e as FamilyInstance;
            return fi != null && fi.Symbol != null && fi.Symbol.Family != null && !fi.Symbol.Family.IsInPlace;
        }

        public static bool CategoryMatches(Element e, HashSet<int> catIds, HashSet<string> catNames)
        {
            var hasId = catIds != null && catIds.Count > 0;
            var hasName = catNames != null && catNames.Count > 0;
            if (!hasId && !hasName) return true;

            var cat = e.Category;
            if (cat == null) return false;

            bool idOk = !hasId || catIds.Contains(cat.Id.IntegerValue);
            bool nameOk = !hasName || catNames.Contains(cat.Name, StringComparer.OrdinalIgnoreCase);

            if (hasId && hasName) return idOk || nameOk;   // OR 許容
            return idOk && nameOk;                         // 片方のみ指定
        }

        public static FamilySymbol ResolveSymbolByArgs(Document doc, JObject p)
        {
            // typeId 優先
            if (p.TryGetValue("typeId", out var tidTok))
            {
                var sym = doc.GetElement(new ElementId(tidTok.Value<int>())) as FamilySymbol;
                if (sym != null && sym.Family != null && !sym.Family.IsInPlace) return sym;
            }

            // typeName + (categoryId | categoryName)
            if (p.TryGetValue("typeName", out var tnameTok))
            {
                string tname = tnameTok.Value<string>() ?? string.Empty;
                int? catId = null;
                string catName = null;

                if (p.TryGetValue("categoryId", out var catIdTok)) catId = catIdTok.Value<int>();
                if (p.TryGetValue("categoryName", out var catNameTok)) catName = catNameTok.Value<string>();

                var q = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(s => s.Family != null && !s.Family.IsInPlace);

                if (catId.HasValue)
                    q = q.Where(s => s.Category != null && s.Category.Id.IntegerValue == catId.Value);
                else if (!string.IsNullOrWhiteSpace(catName))
                    q = q.Where(s => s.Category != null && string.Equals(s.Category.Name, catName, StringComparison.OrdinalIgnoreCase));

                var sym2 = q.FirstOrDefault(s => string.Equals(s.Name, tname, StringComparison.OrdinalIgnoreCase));
                if (sym2 != null) return sym2;
            }

            throw new InvalidOperationException("FamilySymbol が解決できません（typeId または typeName + (categoryId|categoryName) を確認）。");
        }

        public static Level ResolveLevel(Document doc, JObject p)
        {
            if (p.TryGetValue("levelId", out var lidTok))
            {
                var lvl = doc.GetElement(new ElementId(lidTok.Value<int>())) as Level;
                if (lvl != null) return lvl;
            }
            if (p.TryGetValue("levelName", out var lnameTok))
            {
                var lvl = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .FirstOrDefault(l => string.Equals(l.Name, lnameTok.Value<string>(), StringComparison.OrdinalIgnoreCase));
                if (lvl != null) return lvl;
            }
            var any = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault();
            if (any == null) throw new InvalidOperationException("レベルが見つかりません (levelId/levelName を指定してください)。");
            return any;
        }

        public static Element ResolveElement(Document doc, JObject p)
        {
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) return doc.GetElement(new ElementId(eid));
            if (!string.IsNullOrWhiteSpace(uid)) return doc.GetElement(uid);
            return null;
        }
    }

    // -------------------------
    // 一覧
    // -------------------------
    public class GetFamilyInstancesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_family_instances";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            int legacySkip = p.Value<int?>("skip") ?? 0;
            int legacyCount = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;
            var shape = p["_shape"] as JObject;
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
            var pageObj = shape?["page"] as JObject;
            int limit = Math.Max(0, pageObj?.Value<int?>("limit") ?? legacyCount);
            int skip = Math.Max(0, pageObj?.Value<int?>("skip") ?? pageObj?.Value<int?>("offset") ?? legacySkip);
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;

            int targetEid = p.Value<int?>("elementId") ?? 0;
            string targetUid = p.Value<string>("uniqueId");

            HashSet<int> catIds = null;
            HashSet<string> catNames = null;
            if (p.TryGetValue("categoryIds", out var arrIdTok) && arrIdTok is JArray) catIds = new HashSet<int>(((JArray)arrIdTok).Values<int>());
            if (p.TryGetValue("categoryNames", out var arrTok) && arrTok is JArray) catNames = new HashSet<string>(((JArray)arrTok).Values<string>(), StringComparer.OrdinalIgnoreCase);
            string nameContains = p.Value<string>("nameContains");

            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Where(FamUtil.IsLoadableFamilyInstance)
                .Where(fi => FamUtil.CategoryMatches(fi, catIds, catNames))
                .ToList();

            IEnumerable<FamilyInstance> q = all;

            if (targetEid > 0 || !string.IsNullOrWhiteSpace(targetUid))
            {
                FamilyInstance target = null;
                if (targetEid > 0) target = doc.GetElement(new ElementId(targetEid)) as FamilyInstance;
                else target = doc.GetElement(targetUid) as FamilyInstance;
                if (target != null && FamUtil.IsLoadableFamilyInstance(target) && FamUtil.CategoryMatches(target, catIds, catNames))
                    q = new[] { target };
                else
                    q = Enumerable.Empty<FamilyInstance>();
            }

            if (!string.IsNullOrWhiteSpace(nameContains))
            {
                q = q.Where(fi =>
                {
                    var instName = fi.Name ?? "";
                    if (instName.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    var tName = fi.Symbol?.Name ?? "";
                    return tName.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0;
                });
            }

            var ordered = q
                .Select(fi => new { fi, cat = fi.Category?.Name ?? "", tName = fi.Symbol?.Name ?? "" })
                .OrderBy(x => x.cat).ThenBy(x => x.tName).ThenBy(x => x.fi.Id.IntegerValue)
                .Select(x => x.fi)
                .ToList();

            int totalCount = ordered.Count;

            if (summaryOnly || limit == 0)
                return new { ok = true, totalCount, inputUnits = FamUtil.UnitsIn(), internalUnits = FamUtil.UnitsInt() };

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(limit).Select(fi =>
                {
                    var n = fi.Name ?? "";
                    if (!string.IsNullOrEmpty(n)) return n;
                    return fi.Symbol?.Name ?? "";
                }).ToList();

                return new { ok = true, totalCount, names, inputUnits = FamUtil.UnitsIn(), internalUnits = FamUtil.UnitsInt() };
            }

            IEnumerable<FamilyInstance> seq = ordered;
            if (skip > 0) seq = seq.Skip(skip);
            if (limit > 0 && limit != int.MaxValue) seq = seq.Take(limit);

            if (idsOnly)
            {
                var ids = seq.Select(fi => fi.Id.IntegerValue).ToList();
                return new { ok = true, totalCount, elementIds = ids, inputUnits = FamUtil.UnitsIn(), internalUnits = FamUtil.UnitsInt() };
            }

            var page = seq.Select(fi =>
            {
                double x = 0, y = 0, z = 0;
                var lp = fi.Location as LocationPoint;
                if (lp != null && lp.Point != null)
                {
                    x = Math.Round(FamUtil.ToMm(lp.Point.X), 3);
                    y = Math.Round(FamUtil.ToMm(lp.Point.Y), 3);
                    z = Math.Round(FamUtil.ToMm(lp.Point.Z), 3);
                }
                var lvl = fi.LevelId != ElementId.InvalidElementId ? doc.GetElement(fi.LevelId) as Level : null;

                return new
                {
                    elementId = fi.Id.IntegerValue,
                    uniqueId = fi.UniqueId,
                    categoryId = fi.Category?.Id.IntegerValue,
                    categoryName = fi.Category?.Name ?? "",
                    familyName = fi.Symbol?.Family?.Name ?? "",
                    typeId = fi.Symbol?.Id.IntegerValue,
                    typeName = fi.Symbol?.Name ?? "",
                    levelId = fi.LevelId?.IntegerValue,
                    levelName = lvl?.Name ?? "",
                    location = new { x, y, z }
                };
            }).ToList();

            return new { ok = true, totalCount, instances = page, inputUnits = FamUtil.UnitsIn(), internalUnits = FamUtil.UnitsInt() };
        }
    }

    // -------------------------
    // タイプ一覧
    // -------------------------
    public class GetFamilyTypesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_family_types";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;
            // shape/paging (optional overrides)
            var shape = p["_shape"] as JObject;
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
            var pageObj = shape?["page"] as JObject;
            if (pageObj != null)
            {
                var l = pageObj.Value<int?>("limit");
                if (l.HasValue && l.Value < count) count = l.Value;
                var s2 = pageObj.Value<int?>("skip") ?? pageObj.Value<int?>("offset");
                if (s2.HasValue) skip = System.Math.Max(0, s2.Value);
            }
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;

            HashSet<int> catIds = null;
            HashSet<string> catNames = null;
            if (p.TryGetValue("categoryIds", out var arrIdTok) && arrIdTok is JArray) catIds = new HashSet<int>(((JArray)arrIdTok).Values<int>());
            if (p.TryGetValue("categoryNames", out var arrTok) && arrTok is JArray) catNames = new HashSet<string>(((JArray)arrTok).Values<string>(), StringComparer.OrdinalIgnoreCase);
            string nameContains = p.Value<string>("nameContains");

            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .Where(s => s.Family != null && !s.Family.IsInPlace)
                .Where(s => FamUtil.CategoryMatches(s, catIds, catNames))
                .ToList();

            if (!string.IsNullOrWhiteSpace(nameContains))
                all = all.Where(s => (s.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0
                                   || (s.Family?.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            var ordered = all
                .Select(s => new { s, fam = s.Family?.Name ?? "", cat = s.Category?.Name ?? "", name = s.Name ?? "", id = s.Id.IntegerValue })
                .OrderBy(x => x.cat).ThenBy(x => x.fam).ThenBy(x => x.name).ThenBy(x => x.id)
                .Select(x => x.s)
                .ToList();

            int totalCount = ordered.Count;

            if (summaryOnly || count == 0)
                return new { ok = true, totalCount, inputUnits = FamUtil.UnitsIn(), internalUnits = FamUtil.UnitsInt() };

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(s => s.Name ?? "").ToList();
                return new { ok = true, totalCount, names, inputUnits = FamUtil.UnitsIn(), internalUnits = FamUtil.UnitsInt() };
            }

            var seq = ordered.Skip(skip);
            if (count > 0 && count != int.MaxValue) seq = seq.Take(count);
            if (idsOnly)
            {
                var typeIds = seq.Select(s => s.Id.IntegerValue).ToList();
                return new { ok = true, totalCount, typeIds, inputUnits = FamUtil.UnitsIn(), internalUnits = FamUtil.UnitsInt() };
            }

            var page = seq.Select(s => new
            {
                typeId = s.Id.IntegerValue,
                uniqueId = s.UniqueId,
                typeName = s.Name ?? "",
                familyName = s.Family?.Name ?? "",
                categoryId = s.Category?.Id.IntegerValue,
                categoryName = s.Category?.Name ?? ""
            }).ToList();

            return new { ok = true, totalCount, types = page, inputUnits = FamUtil.UnitsIn(), internalUnits = FamUtil.UnitsInt() };
        }
    }

    // -------------------------
    // 作成
    // -------------------------
    public class CreateFamilyInstanceCommand : IRevitCommandHandler
    {
        public string CommandName => "create_family_instance";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            var sym = FamUtil.ResolveSymbolByArgs(doc, p);
            var lvl = FamUtil.ResolveLevel(doc, p);

            var loc = p["location"] as JObject;
            if (loc == null) return new { ok = false, msg = "location が必要です（mm）。" };

            var pos = new XYZ(
                FamUtil.MmToFt(loc.Value<double>("x")),
                FamUtil.MmToFt(loc.Value<double>("y")),
                FamUtil.MmToFt(loc.Value<double>("z"))
            );

            FamilyInstance inst = null;
            using (var tx = new Transaction(doc, "Create Family Instance"))
            {
                tx.Start();
                if (!sym.IsActive) sym.Activate();

                // host 任意
                Element host = null;
                if (p.TryGetValue("hostId", out var hostTok))
                    host = doc.GetElement(new ElementId(hostTok.Value<int>()));
                else if (p.TryGetValue("hostUniqueId", out var hostUidTok))
                    host = doc.GetElement(hostUidTok.Value<string>());

                var stype = StructuralType.NonStructural;

                if (host != null)
                    inst = doc.Create.NewFamilyInstance(pos, sym, host, lvl, stype);
                else
                    inst = doc.Create.NewFamilyInstance(pos, sym, lvl, stype);

                // 回転（deg）
                if (p.TryGetValue("rotationDeg", out var rotTok))
                {
                    double deg = rotTok.Value<double>();
                    var axis = Line.CreateBound(pos, pos + XYZ.BasisZ);
                    ElementTransformUtils.RotateElement(doc, inst.Id, axis, FamUtil.DegToRadSafe(deg));
                }

                // フリップ（存在時のみ）
                if (p.Value<bool?>("flipFacing") == true) { try { inst?.flipFacing(); } catch { } }
                if (p.Value<bool?>("flipHand") == true) { try { inst?.flipHand(); } catch { } }

                tx.Commit();
            }
            return new
            {
                ok = true,
                elementId = inst.Id.IntegerValue,
                uniqueId = inst.UniqueId,
                typeId = inst.Symbol?.Id.IntegerValue,
                inputUnits = FamUtil.UnitsIn(),
                internalUnits = FamUtil.UnitsInt()
            };
        }
    }

    // -------------------------
    // 移動
    // -------------------------
    public class MoveFamilyInstanceCommand : IRevitCommandHandler
    {
        public string CommandName => "move_family_instance";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            var el = FamUtil.ResolveElement(doc, p);
            if (el == null) return new { ok = false, msg = "要素が見つかりません（elementId/uniqueId）。" };

            XYZ offset;
            if (p.TryGetValue("offset", out var offTok) && offTok is JObject off)
                offset = new XYZ(FamUtil.MmToFt(off.Value<double>("x")), FamUtil.MmToFt(off.Value<double>("y")), FamUtil.MmToFt(off.Value<double>("z")));
            else
                offset = new XYZ(FamUtil.MmToFt(p.Value<double?>("dx") ?? 0), FamUtil.MmToFt(p.Value<double?>("dy") ?? 0), FamUtil.MmToFt(p.Value<double?>("dz") ?? 0));

            using (var tx = new Transaction(doc, "Move Family Instance"))
            {
                tx.Start();
                ElementTransformUtils.MoveElement(doc, el.Id, offset);
                tx.Commit();
            }
            return new { ok = true, elementId = el.Id.IntegerValue, uniqueId = el.UniqueId, inputUnits = new { Length = "mm" }, internalUnits = new { Length = "ft" } };
        }
    }

    // -------------------------
    // 削除
    // -------------------------
    public class DeleteFamilyInstanceCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_family_instance";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            var el = FamUtil.ResolveElement(doc, p);
            if (el == null) return new { ok = false, msg = "要素が見つかりません（elementId/uniqueId）。" };

            ICollection<ElementId> deleted = null;
            using (var tx = new Transaction(doc, "Delete Family Instance"))
            {
                tx.Start();
                deleted = doc.Delete(el.Id);
                tx.Commit();
            }
            var ids = deleted?.Select(x => x.IntegerValue).ToList() ?? new List<int>();
            return new { ok = true, elementId = el.Id.IntegerValue, uniqueId = el.UniqueId, deletedCount = ids.Count, deletedElementIds = ids };
        }
    }

    // -------------------------
    // パラメータ取得（インスタンス）
    // -------------------------
    public class GetFamilyInstanceParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_family_instance_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            var el = FamUtil.ResolveElement(doc, p);
            if (!(el is FamilyInstance fi) || !FamUtil.IsLoadableFamilyInstance(fi))
                return new { ok = false, msg = "FamilyInstance（ロード可能）が見つかりません。" };

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            var ordered = (fi.Parameters?.Cast<Parameter>() ?? Enumerable.Empty<Parameter>())
                .Select(pa => new { pa, name = pa?.Definition?.Name ?? "", id = pa?.Id.IntegerValue ?? -1 })
                .OrderBy(x => x.name).ThenBy(x => x.id).Select(x => x.pa).ToList();

            int totalCount = ordered.Count;

            if (count == 0)
                return new { ok = true, elementId = fi.Id.IntegerValue, uniqueId = fi.UniqueId, totalCount, inputUnits = FamUtil.UnitsIn(), internalUnits = FamUtil.UnitsInt() };

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(pa => pa?.Definition?.Name ?? "").ToList();
                return new { ok = true, elementId = fi.Id.IntegerValue, uniqueId = fi.UniqueId, totalCount, names, inputUnits = FamUtil.UnitsIn(), internalUnits = FamUtil.UnitsInt() };
            }

            var page = ordered.Skip(skip).Take(count);
            var list = new List<object>();
            foreach (var pa in page)
            {
                ForgeTypeId fdt = null; string dataType = null;
                try { fdt = pa.Definition?.GetDataType(); dataType = fdt?.TypeId; } catch { dataType = null; }

                object val = null;
                try
                {
                    switch (pa.StorageType)
                    {
                        case StorageType.Double: val = FamUtil.ConvertDoubleBySpec(pa.AsDouble(), fdt); break;
                        case StorageType.Integer: val = pa.AsInteger(); break;
                        case StorageType.String: val = pa.AsString() ?? string.Empty; break;
                        case StorageType.ElementId: val = pa.AsElementId()?.IntegerValue ?? -1; break;
                    }
                }
                catch { val = null; }

                string guidStr = string.Empty;
                try
                {
                    var def = pa.Definition as ExternalDefinition;
                    if (def != null) guidStr = def.GUID.ToString();
                }
                catch { guidStr = string.Empty; }

                list.Add(new
                {
                    name = pa.Definition?.Name ?? "",
                    id = pa.Id.IntegerValue,
                    storageType = pa.StorageType.ToString(),
                    isReadOnly = pa.IsReadOnly,
                    dataType,
                    guid = guidStr,
                    value = val
                });
            }

            return new { ok = true, elementId = fi.Id.IntegerValue, uniqueId = fi.UniqueId, totalCount, parameters = list, inputUnits = FamUtil.UnitsIn(), internalUnits = FamUtil.UnitsInt() };
        }
    }

    // -------------------------
    // パラメータ設定（インスタンス）
    // -------------------------
    public class UpdateFamilyInstanceParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "update_family_instance_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            var el = FamUtil.ResolveElement(doc, p);
            if (!(el is FamilyInstance fi) || !FamUtil.IsLoadableFamilyInstance(fi))
                return new { ok = false, msg = "FamilyInstance（ロード可能）が見つかりません。" };

            string name = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(name) && p["builtInName"] == null && p["builtInId"] == null && p["guid"] == null)
                return new { ok = false, msg = "paramName または builtInName/builtInId/guid のいずれかが必要です。" };
            if (!p.TryGetValue("value", out var vtok)) return new { ok = false, msg = "value が必要です。" };

            var prm = ParamResolver.ResolveByPayload(fi, p, out var resolvedBy);
            if (prm == null) return new { ok = false, msg = $"Parameter not found (name/builtIn/guid)" };
            if (prm.IsReadOnly) return new { ok = false, msg = $"Parameter '{name}' は読み取り専用です。" };

            // オプション: レベルオフセット等の変更を Z 方向の移動としても反映する
            bool applyOffsetAsMove = p.Value<bool?>("applyOffsetAsMove") ?? false;
            double? deltaOffsetMm = null;
            if (applyOffsetAsMove && prm.StorageType == StorageType.Double)
            {
                try
                {
                    double oldInternal = prm.AsDouble();
                    double oldMm = UnitUtils.ConvertFromInternalUnits(oldInternal, UnitTypeId.Millimeters);
                    double newMm = vtok.Value<double>();
                    double diff = newMm - oldMm;
                    if (Math.Abs(diff) > 1e-6)
                        deltaOffsetMm = diff;
                }
                catch
                {
                    // 差分計算に失敗した場合は位置移動を行わず、従来通りパラメータ更新のみを行う
                    deltaOffsetMm = null;
                }
            }

            using (var tx = new Transaction(doc, $"Set FI Param '{name}'"))
            {
                tx.Start();
                try
                {
                    switch (prm.StorageType)
                    {
                        case StorageType.Double:
                            {
                                ForgeTypeId fdt = null; try { fdt = prm.Definition?.GetDataType(); } catch { fdt = null; }
                                prm.Set(FamUtil.ToInternalBySpec(vtok.Value<double>(), fdt));
                                break;
                            }
                        case StorageType.Integer:
                            prm.Set(vtok.Value<int>());
                            break;
                        case StorageType.String:
                            prm.Set(vtok.Value<string>() ?? string.Empty);
                            break;
                        case StorageType.ElementId:
                            prm.Set(new ElementId(vtok.Value<int>()));
                            break;
                        default:
                            tx.RollBack();
                            return new { ok = false, msg = $"Unsupported StorageType: {prm.StorageType}" };
                    }

                    // 必要に応じて、オフセット差分に応じて FamilyInstance 全体を Z 方向に移動
                    if (deltaOffsetMm.HasValue && Math.Abs(deltaOffsetMm.Value) > 1e-6)
                    {
                        try
                        {
                            var offset = UnitHelper.MmToInternalXYZ(0.0, 0.0, deltaOffsetMm.Value);
                            ElementTransformUtils.MoveElement(doc, fi.Id, offset);
                        }
                        catch (Exception ex)
                        {
                            // 位置移動に失敗してもパラメータ更新自体は成功させる
                            RevitMCPAddin.Core.RevitLogger.Warn($"update_family_instance_parameter: move by offset failed for element {fi.Id.IntegerValue}: {ex.Message}");
                        }
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = ex.Message };
                }
            }
            return new { ok = true, elementId = fi.Id.IntegerValue, uniqueId = fi.UniqueId };
        }
    }

    // -------------------------
    // パラメータ取得（タイプ）
    // -------------------------
    public class GetFamilyTypeParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_family_type_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            FamilySymbol sym = null;
            int typeId = p.Value<int?>("typeId") ?? 0;
            if (typeId > 0) sym = doc.GetElement(new ElementId(typeId)) as FamilySymbol;
            else
            {
                string typeName = p.Value<string>("typeName");
                string categoryName = p.Value<string>("categoryName");
                int? categoryId = p.Value<int?>("categoryId");
                if (!string.IsNullOrWhiteSpace(typeName))
                {
                    var q = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).WhereElementIsElementType()
                        .Cast<FamilySymbol>().Where(FamUtil.IsLoadableFamilyInstance);
                    if (categoryId.HasValue) q = q.Where(s => s.Category?.Id.IntegerValue == categoryId.Value);
                    else if (!string.IsNullOrWhiteSpace(categoryName)) q = q.Where(s => string.Equals(s.Category?.Name ?? "", categoryName, StringComparison.OrdinalIgnoreCase));
                    sym = q.FirstOrDefault(s => string.Equals(s.Name ?? "", typeName, StringComparison.OrdinalIgnoreCase));
                }
            }
            if (sym == null) return new { ok = false, msg = "FamilySymbol が見つかりません（typeId または typeName + (categoryId|categoryName)）。" };

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            var ordered = (sym.Parameters?.Cast<Parameter>() ?? Enumerable.Empty<Parameter>())
                .Select(pa => new { pa, name = pa?.Definition?.Name ?? "", id = pa?.Id.IntegerValue ?? -1 })
                .OrderBy(x => x.name).ThenBy(x => x.id).Select(x => x.pa).ToList();

            int totalCount = ordered.Count;

            if (count == 0)
                return new { ok = true, typeId = sym.Id.IntegerValue, uniqueId = sym.UniqueId, totalCount, inputUnits = FamUtil.UnitsIn(), internalUnits = FamUtil.UnitsInt() };

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(pa => pa?.Definition?.Name ?? "").ToList();
                return new { ok = true, typeId = sym.Id.IntegerValue, uniqueId = sym.UniqueId, totalCount, names, inputUnits = FamUtil.UnitsIn(), internalUnits = FamUtil.UnitsInt() };
            }

            var page = ordered.Skip(skip).Take(count);
            var list = new List<object>();
            foreach (var pa in page)
            {
                ForgeTypeId fdt = null; string dataType = null;
                try { fdt = pa.Definition?.GetDataType(); dataType = fdt?.TypeId; } catch { dataType = null; }

                object val = null;
                try
                {
                    switch (pa.StorageType)
                    {
                        case StorageType.Double: val = FamUtil.ConvertDoubleBySpec(pa.AsDouble(), fdt); break;
                        case StorageType.Integer: val = pa.AsInteger(); break;
                        case StorageType.String: val = pa.AsString() ?? string.Empty; break;
                        case StorageType.ElementId: val = pa.AsElementId()?.IntegerValue ?? -1; break;
                    }
                }
                catch { val = null; }

                // Shared parameter GUID (if ExternalDefinition)
                string guidStr = string.Empty;
                try
                {
                    var def = pa.Definition as ExternalDefinition;
                    if (def != null) guidStr = def.GUID.ToString();
                }
                catch { guidStr = string.Empty; }

                list.Add(new
                {
                    name = pa.Definition?.Name ?? "",
                    id = pa.Id.IntegerValue,
                    storageType = pa.StorageType.ToString(),
                    isReadOnly = pa.IsReadOnly,
                    dataType,
                    guid = guidStr,
                    value = val
                });
            }

            return new { ok = true, typeId = sym.Id.IntegerValue, uniqueId = sym.UniqueId, totalCount, parameters = list, inputUnits = FamUtil.UnitsIn(), internalUnits = FamUtil.UnitsInt() };
        }
    }

    // -------------------------
    // パラメータ設定（タイプ）
    // -------------------------
    public class SetFamilyTypeParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "set_family_type_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            FamilySymbol sym = null;
            int typeId = p.Value<int?>("typeId") ?? 0;
            if (typeId > 0) sym = doc.GetElement(new ElementId(typeId)) as FamilySymbol;
            else
            {
                string typeName = p.Value<string>("typeName");
                string categoryName = p.Value<string>("categoryName");
                int? categoryId = p.Value<int?>("categoryId");
                if (!string.IsNullOrWhiteSpace(typeName))
                {
                    var q = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).WhereElementIsElementType()
                        .Cast<FamilySymbol>().Where(FamUtil.IsLoadableFamilyInstance);
                    if (categoryId.HasValue) q = q.Where(s => s.Category?.Id.IntegerValue == categoryId.Value);
                    else if (!string.IsNullOrWhiteSpace(categoryName)) q = q.Where(s => string.Equals(s.Category?.Name ?? "", categoryName, StringComparison.OrdinalIgnoreCase));
                    sym = q.FirstOrDefault(s => string.Equals(s.Name ?? "", typeName, StringComparison.OrdinalIgnoreCase));
                }
            }
            if (sym == null) return new { ok = false, msg = "FamilySymbol が見つかりません（typeId または typeName + (categoryId|categoryName)）。" };

            string name = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(name) && p["builtInName"] == null && p["builtInId"] == null && p["guid"] == null)
                return new { ok = false, msg = "paramName または builtInName/builtInId/guid のいずれかが必要です。" };
            if (!p.TryGetValue("value", out var vtok)) return new { ok = false, msg = "value が必要です。" };

            var prm = ParamResolver.ResolveByPayload(sym, p, out var resolvedBy2);
            if (prm == null) return new { ok = false, msg = $"Parameter not found (name/builtIn/guid)" };
            if (prm.IsReadOnly) return new { ok = false, msg = $"Parameter '{name}' は読み取り専用です。" };

            using (var tx = new Transaction(doc, $"Set Type Param '{name}'"))
            {
                tx.Start();
                try
                {
                    switch (prm.StorageType)
                    {
                        case StorageType.Double:
                            {
                                ForgeTypeId fdt = null; try { fdt = prm.Definition?.GetDataType(); } catch { fdt = null; }
                                prm.Set(FamUtil.ToInternalBySpec(vtok.Value<double>(), fdt));
                                break;
                            }
                        case StorageType.Integer: prm.Set(vtok.Value<int>()); break;
                        case StorageType.String: prm.Set(vtok.Value<string>() ?? string.Empty); break;
                        case StorageType.ElementId: prm.Set(new ElementId(vtok.Value<int>())); break;
                        default: tx.RollBack(); return new { ok = false, msg = $"Unsupported StorageType: {prm.StorageType}" };
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = ex.Message };
                }
            }
            return new { ok = true, typeId = sym.Id.IntegerValue, uniqueId = sym.UniqueId };
        }
    }
}
