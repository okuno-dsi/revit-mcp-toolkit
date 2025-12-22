// ================================================================
// File: Commands/ZoneOps/ZoneCommands.cs
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Purpose: Revit MEP Zone 操作 一式（9コマンドを1ファイルに集約）
// Notes  : 単位変換は UnitUtils を使用（mm/m2/m3/deg）
//          例外時は { ok:false, msg } を返すポリシー
//          参照API: Zone.AddSpaces/RemoveSpaces(SpaceSet), doc.Create.NewZone(Level, Phase)
// ================================================================
#nullable enable
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCPAddin.Commands.ZoneOps
{
    // -------------------------- 共通ユーティリティ --------------------------
    internal static class ZoneUtil
    {
        // ---- Level / Zone 解決 ----
        public static Level? ResolveLevel(Document doc, int? levelId, string? levelName)
        {
            if (levelId.HasValue)
            {
                if (doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(levelId.Value)) is Level l1) return l1;
            }
            if (!string.IsNullOrWhiteSpace(levelName))
            {
                foreach (var e in new FilteredElementCollector(doc).OfClass(typeof(Level)))
                {
                    if (e is Level l2 && string.Equals(l2.Name, levelName, StringComparison.OrdinalIgnoreCase)) return l2;
                }
            }
            return null;
        }

        public static Phase? ResolvePhase(Document doc)
        {
            // 優先：アクティブビューの Phase、無ければ最初のPhase
            try
            {
                var av = doc.ActiveView;
                var pid = av?.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsElementId();
                if (pid != null && pid.IntValue() > 0)
                    return doc.GetElement(pid) as Phase;
            }
            catch { /* ignore */ }

            return new FilteredElementCollector(doc).OfClass(typeof(Phase)).Cast<Phase>().FirstOrDefault();
        }

        public static Zone? ResolveZone(Document doc, int? zoneId, string? zoneUniqueId)
        {
            if (zoneId.HasValue)
            {
                if (doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(zoneId.Value)) is Zone z1) return z1;
            }
            if (!string.IsNullOrWhiteSpace(zoneUniqueId))
            {
                return doc.GetElement(zoneUniqueId) as Zone;
            }
            return null;
        }

        public static IEnumerable<Autodesk.Revit.DB.Mechanical.Space> ResolveSpaces(Document doc, IEnumerable<int> spaceIds)
        {
            if (spaceIds == null) yield break;
            var seen = new HashSet<int>();
            foreach (var i in spaceIds)
            {
                if (!seen.Add(i)) continue;
                if (doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(i)) is Autodesk.Revit.DB.Mechanical.Space sp) yield return sp;
            }
        }

        // ---- Zone Number の get/set（BIP 無いため Lookup 優先）----
        public static string GetZoneNumber(Zone z)
        {
            var v = z.LookupParameter("Number")?.AsString();
            if (!string.IsNullOrEmpty(v)) return v!;
            v = z.LookupParameter("Zone Number")?.AsString();
            return v ?? "";
        }
        public static void SetZoneNumber(Zone z, string value)
        {
            var p = z.LookupParameter("Number") ?? z.LookupParameter("Zone Number");
            p?.Set(value);
        }

        // ---- 単位変換（SI）----
        public static double FtToMm(double v) => UnitUtils.ConvertFromInternalUnits(v, UnitTypeId.Millimeters);
        public static double Ft2ToM2(double v) => UnitUtils.ConvertFromInternalUnits(v, UnitTypeId.SquareMeters);
        public static double Ft3ToM3(double v) => UnitUtils.ConvertFromInternalUnits(v, UnitTypeId.CubicMeters);
        public static double DegToRad(double deg) => UnitUtils.ConvertToInternalUnits(deg, UnitTypeId.Degrees);
        public static double MmToFt(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        public static double M2ToFt2(double m2) => UnitUtils.ConvertToInternalUnits(m2, UnitTypeId.SquareMeters);
        public static double M3ToFt3(double m3) => UnitUtils.ConvertToInternalUnits(m3, UnitTypeId.CubicMeters);

        public static double GetParamAsDisplayDouble(Parameter p)
        {
            try
            {
                var dt = p.Definition?.GetDataType();
                if (dt != null)
                {
                    if (dt.Equals(SpecTypeId.Length)) return FtToMm(p.AsDouble());
                    if (dt.Equals(SpecTypeId.Area)) return Ft2ToM2(p.AsDouble());
                    if (dt.Equals(SpecTypeId.Volume)) return Ft3ToM3(p.AsDouble());
                    if (dt.Equals(SpecTypeId.Angle)) return UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Degrees);
                }
                return p.AsDouble();
            }
            catch { return p.AsDouble(); }
        }

        public static (bool ok, string? err) SetParamFromDisplay(Parameter p, JToken? valueToken)
        {
            try
            {
                if (p.IsReadOnly) return (false, $"パラメータ '{p.Definition?.Name}' は読み取り専用です。");
                switch (p.StorageType)
                {
                    case StorageType.String:
                        p.Set((string?)valueToken?.ToObject<string>() ?? "");
                        return (true, null);

                    case StorageType.Integer:
                        if (valueToken == null) return (false, "Integer 値が空です。");
                        int iv = valueToken.Type == JTokenType.Boolean ? ((bool)valueToken ? 1 : 0) : valueToken.ToObject<int>();
                        p.Set(iv);
                        return (true, null);

                    case StorageType.Double:
                        {
                            if (valueToken == null) return (false, "Double 値が空です。");
                            double dv = valueToken.ToObject<double>();
                            try
                            {
                                var dt = p.Definition?.GetDataType();
                                if (dt != null)
                                {
                                    if (dt.Equals(SpecTypeId.Length)) { p.Set(MmToFt(dv)); return (true, null); }
                                    if (dt.Equals(SpecTypeId.Area)) { p.Set(M2ToFt2(dv)); return (true, null); }
                                    if (dt.Equals(SpecTypeId.Volume)) { p.Set(M3ToFt3(dv)); return (true, null); }
                                    if (dt.Equals(SpecTypeId.Angle)) { p.Set(DegToRad(dv)); return (true, null); }
                                }
                            }
                            catch { /* raw にフォールバック */ }
                            p.Set(dv);
                            return (true, null);
                        }

                    case StorageType.ElementId:
                        {
                            if (valueToken == null) return (false, "ElementId 値が空です。");
                            p.Set(Autodesk.Revit.DB.ElementIdCompat.From(valueToken.ToObject<int>()));
                            return (true, null);
                        }

                    default:
                        return (false, $"未対応の StorageType: {p.StorageType}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"SetParam 例外: {ex.Message}");
            }
        }

        public static (double x, double y, double z) CenterMmByBBox(Element e)
        {
            try
            {
                var bb = e.get_BoundingBox(null);
                if (bb == null) return (0, 0, 0);
                var cx = (bb.Min.X + bb.Max.X) * 0.5;
                var cy = (bb.Min.Y + bb.Max.Y) * 0.5;
                var cz = (bb.Min.Z + bb.Max.Z) * 0.5;
                return (FtToMm(cx), FtToMm(cy), FtToMm(cz));
            }
            catch { return (0, 0, 0); }
        }
    }

    // -------------------------- 1) get_zones --------------------------
    public class GetZonesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_zones";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params;
            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? 100;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;
            int? levelId = p.Value<int?>("levelId");
            string? levelName = p.Value<string?>("levelName");
            string? nameContains = p.Value<string?>("nameContains");
            string? numberContains = p.Value<string?>("numberContains");

            try
            {
                ElementId? filterLevelId = null;
                if (levelId.HasValue) filterLevelId = Autodesk.Revit.DB.ElementIdCompat.From(levelId.Value);
                else if (!string.IsNullOrWhiteSpace(levelName))
                {
                    var lvl = ZoneUtil.ResolveLevel(doc, null, levelName);
                    if (lvl != null) filterLevelId = lvl.Id;
                }

                var zones = new FilteredElementCollector(doc)
                    .OfClass(typeof(Zone))
                    .Cast<Zone>()
                    .ToList();

                // 名前・番号フィルタ
                if (!string.IsNullOrEmpty(nameContains))
                    zones = zones.Where(z => (z.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                if (!string.IsNullOrEmpty(numberContains))
                    zones = zones.Where(z => ZoneUtil.GetZoneNumber(z).IndexOf(numberContains, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                // レベルフィルタ：ゾーン内のスペースの Level によって判定
                if (filterLevelId != null)
                {
                    zones = zones.Where(z =>
                    {
                        var ids = z.Spaces;
                        if (ids == null) return false;
                        foreach (ElementId sid in ids)
                        {
                            if (doc.GetElement(sid) is Autodesk.Revit.DB.Mechanical.Space sp)
                            {
                                if (sp.LevelId != null && sp.LevelId == filterLevelId) return true;
                            }
                        }
                        return false;
                    }).ToList();
                }

                var total = zones.Count;
                var page = zones.Skip(skip).Take(count).ToList();

                var items = new List<object>(page.Count);
                foreach (var z in page)
                {
                    double areaM2 = 0.0, volM3 = 0.0;
                    int spaceCount = 0;

                    ElementId? firstLevelId = null;
                    string? firstLevelName = null;

                    var ids = z.Spaces;
                    if (ids != null)
                    {
                        foreach (ElementId sid in ids)
                        {
                            if (!(doc.GetElement(sid) is Autodesk.Revit.DB.Mechanical.Space sp)) continue;
                            spaceCount++;
                            areaM2 += ZoneUtil.Ft2ToM2(sp.Area);
                            volM3 += ZoneUtil.Ft3ToM3(sp.Volume);
                            if (firstLevelId == null && sp.LevelId != null)
                            {
                                firstLevelId = sp.LevelId;
                                firstLevelName = sp.Level?.Name;
                            }
                        }
                    }

                    if (namesOnly)
                    {
                        items.Add(new { zoneId = z.Id.IntValue(), name = z.Name });
                    }
                    else
                    {
                        items.Add(new
                        {
                            zoneId = z.Id.IntValue(),
                            uniqueId = z.UniqueId,
                            name = z.Name,
                            number = ZoneUtil.GetZoneNumber(z),
                            levelId = firstLevelId?.IntValue(),
                            levelName = firstLevelName,
                            spaceCount,
                            areaM2,
                            volumeM3 = volM3
                        });
                    }
                }

                return new { ok = true, totalCount = total, zones = items };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "get_zones 実行中に例外: " + ex.Message };
            }
        }
    }

    // -------------------------- 2) create_zone --------------------------
    public class CreateZoneCommand : IRevitCommandHandler
    {
        public string CommandName => "create_zone";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params;
            int? levelId = p.Value<int?>("levelId");
            string? levelName = p.Value<string?>("levelName");
            string? name = p.Value<string?>("name");
            string? number = p.Value<string?>("number");
            var initialSpaceIds = p["initialSpaceIds"]?.ToObject<List<int>>() ?? new List<int>();

            try
            {
                var lvl = ZoneUtil.ResolveLevel(doc, levelId, levelName);
                if (lvl == null) return new { ok = false, msg = "レベルが特定できません（levelId / levelName を確認）。" };

                var phase = ZoneUtil.ResolvePhase(doc);
                if (phase == null) return new { ok = false, msg = "Phase が見つかりません（ビューのフェーズまたはプロジェクトの Phase を確認）。" };

                Zone? created = null;
                int addedCount = 0;

                using (var t = new Transaction(doc, "Create Zone"))
                {
                    t.Start();

                    // 旧API: Document.NewZone(Level, Phase)
                    created = doc.Create.NewZone(lvl, phase);
                    if (created == null)
                    {
                        t.RollBack();
                        return new { ok = false, msg = "Zone の作成に失敗しました。" };
                    }

                    if (!string.IsNullOrWhiteSpace(name))
                        created.LookupParameter("Name")?.Set(name);
                    if (!string.IsNullOrWhiteSpace(number))
                        ZoneUtil.SetZoneNumber(created, number);

                    // まとめて追加: Zone.AddSpaces(SpaceSet)
                    if (initialSpaceIds.Count > 0)
                    {
                        using (var ss = new SpaceSet())
                        {
                            foreach (var sp in ZoneUtil.ResolveSpaces(doc, initialSpaceIds))
                                ss.Insert(sp);

                            if (ss.Size > 0 && created.AddSpaces(ss)) addedCount = (int)ss.Size;
                        }
                    }

                    t.Commit();
                }

                return new
                {
                    ok = true,
                    zoneId = created?.Id.IntValue(),
                    uniqueId = created?.UniqueId,
                    levelId = lvl.Id.IntValue(),
                    name = created?.Name,
                    number = created != null ? ZoneUtil.GetZoneNumber(created) : "",
                    addedSpaces = addedCount
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "create_zone 実行中に例外: " + ex.Message };
            }
        }
    }

    // -------------------------- 3) add_spaces_to_zone --------------------------
    public class AddSpacesToZoneCommand : IRevitCommandHandler
    {
        public string CommandName => "add_spaces_to_zone";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params;
            int? zoneId = p.Value<int?>("zoneId");
            string? zoneUniqueId = p.Value<string?>("zoneUniqueId");
            var spaceIds = p["spaceIds"]?.ToObject<List<int>>() ?? new List<int>();

            try
            {
                var zone = ZoneUtil.ResolveZone(doc, zoneId, zoneUniqueId);
                if (zone == null) return new { ok = false, msg = "Zone が見つかりません。" };
                if (spaceIds.Count == 0) return new { ok = false, msg = "追加対象の spaceIds が空です。" };

                int added = 0;
                var skipped = new List<object>();

                using (var t = new Transaction(doc, "Add Spaces to Zone"))
                {
                    t.Start();
                    using (var ss = new SpaceSet())
                    {
                        foreach (var sp in ZoneUtil.ResolveSpaces(doc, spaceIds)) ss.Insert(sp);
                        if (ss.Size > 0)
                        {
                            bool ok = zone.AddSpaces(ss); // bool 戻り
                            added = ok ? (int)ss.Size : 0;
                            if (!ok) skipped.Add(new { reason = "Zone.AddSpaces が false を返しました。" });
                        }
                    }
                    t.Commit();
                }

                return new { ok = true, zoneId = zone.Id.IntValue(), added, skipped };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "add_spaces_to_zone 実行中に例外: " + ex.Message };
            }
        }
    }

    // -------------------------- 4) remove_spaces_from_zone --------------------------
    public class RemoveSpacesFromZoneCommand : IRevitCommandHandler
    {
        public string CommandName => "remove_spaces_from_zone";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params;
            int? zoneId = p.Value<int?>("zoneId");
            string? zoneUniqueId = p.Value<string?>("zoneUniqueId");
            var spaceIds = p["spaceIds"]?.ToObject<List<int>>() ?? new List<int>();

            try
            {
                var zone = ZoneUtil.ResolveZone(doc, zoneId, zoneUniqueId);
                if (zone == null) return new { ok = false, msg = "Zone が見つかりません。" };
                if (spaceIds.Count == 0) return new { ok = false, msg = "削除対象の spaceIds が空です。" };

                int removed = 0;
                var skipped = new List<object>();

                using (var t = new Transaction(doc, "Remove Spaces from Zone"))
                {
                    t.Start();
                    using (var ss = new SpaceSet())
                    {
                        foreach (var sp in ZoneUtil.ResolveSpaces(doc, spaceIds)) ss.Insert(sp);
                        if (ss.Size > 0)
                        {
                            bool ok = zone.RemoveSpaces(ss);
                            removed = ok ? (int)ss.Size : 0;
                            if (!ok) skipped.Add(new { reason = "Zone.RemoveSpaces が false を返しました。" });
                        }
                    }
                    t.Commit();
                }

                return new { ok = true, zoneId = zone.Id.IntValue(), removed, skipped };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "remove_spaces_from_zone 実行中に例外: " + ex.Message };
            }
        }
    }

    // -------------------------- 5) delete_zone --------------------------
    public class DeleteZoneCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_zone";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params;
            int? zoneId = p.Value<int?>("zoneId");
            string? zoneUniqueId = p.Value<string?>("zoneUniqueId");

            try
            {
                var zone = ZoneUtil.ResolveZone(doc, zoneId, zoneUniqueId);
                if (zone == null) return new { ok = false, msg = "Zone が見つかりません。" };

                ICollection<ElementId> deleted = new List<ElementId>();
                using (var t = new Transaction(doc, "Delete Zone"))
                {
                    t.Start();
                    deleted = doc.Delete(zone.Id);
                    t.Commit();
                }

                return new
                {
                    ok = true,
                    deletedCount = deleted?.Count ?? 0,
                    deletedElementIds = (deleted ?? Array.Empty<ElementId>()).Select(x => x.IntValue()).ToList()
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "delete_zone 実行中に例外: " + ex.Message };
            }
        }
    }

    // -------------------------- 6) list_zone_members --------------------------
    public class ListZoneMembersCommand : IRevitCommandHandler
    {
        public string CommandName => "list_zone_members";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params;
            int? zoneId = p.Value<int?>("zoneId");
            string? zoneUniqueId = p.Value<string?>("zoneUniqueId");

            try
            {
                var zone = ZoneUtil.ResolveZone(doc, zoneId, zoneUniqueId);
                if (zone == null) return new { ok = false, msg = "Zone が見つかりません。" };

                var items = new List<object>();
                var ids = zone.Spaces;
                if (ids != null)
                {
                    foreach (ElementId sid in ids)
                    {
                        if (!(doc.GetElement(sid) is Autodesk.Revit.DB.Mechanical.Space sp)) continue;
                        var center = ZoneUtil.CenterMmByBBox(sp);
                        items.Add(new
                        {
                            elementId = sp.Id.IntValue(),
                            uniqueId = sp.UniqueId,
                            number = sp.Number,
                            name = sp.Name,
                            levelId = sp.LevelId?.IntValue(),
                            levelName = sp.Level?.Name,
                            areaM2 = ZoneUtil.Ft2ToM2(sp.Area),
                            volumeM3 = ZoneUtil.Ft3ToM3(sp.Volume),
                            center = new { x = center.x, y = center.y, z = center.z }
                        });
                    }
                }

                return new { ok = true, zoneId = zone.Id.IntValue(), count = items.Count, spaces = items };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "list_zone_members 実行中に例外: " + ex.Message };
            }
        }
    }

    // -------------------------- 7) get_zone_params --------------------------
    public class GetZoneParamsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_zone_params";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params;
            int? zoneId = p.Value<int?>("zoneId");
            string? zoneUniqueId = p.Value<string?>("zoneUniqueId");
            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? 100;
            bool includeDisplay = p.Value<bool?>("includeDisplay") ?? true;
            bool includeRaw = p.Value<bool?>("includeRaw") ?? true;
            bool includeUnit = p.Value<bool?>("includeUnit") ?? true;
            string? orderBy = p.Value<string?>("orderBy");
            bool desc = p.Value<bool?>("desc") ?? false;
            string? nameContains = p.Value<string?>("nameContains");

            try
            {
                var zone = ZoneUtil.ResolveZone(doc, zoneId, zoneUniqueId);
                if (zone == null) return new { ok = false, msg = "Zone が見つかりません。" };

                var list = new List<Parameter>();
                foreach (Parameter ap in zone.Parameters) list.Add(ap);

                if (!string.IsNullOrEmpty(nameContains))
                    list = list.Where(x => (x.Definition?.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                list = (orderBy?.ToLowerInvariant()) switch
                {
                    "name" => (desc ? list.OrderByDescending(x => x.Definition?.Name) : list.OrderBy(x => x.Definition?.Name)).ToList(),
                    "readonly" => (desc ? list.OrderByDescending(x => x.IsReadOnly) : list.OrderBy(x => x.IsReadOnly)).ToList(),
                    _ => list.OrderBy(x => x.Definition?.Name).ToList()
                };

                var total = list.Count;
                var page = list.Skip(skip).Take(count).ToList();
                var result = new List<object>(page.Count);

                foreach (var ap in page)
                {
                    object? value = null;
                    string? unit = null;
                    string? display = null;

                    switch (ap.StorageType)
                    {
                        case StorageType.String:
                            value = includeRaw ? (object?)ap.AsString() : null;
                            display = includeDisplay ? ap.AsValueString() : null;
                            break;

                        case StorageType.Integer:
                            value = includeRaw ? (object?)ap.AsInteger() : null;
                            display = includeDisplay ? ap.AsValueString() : null;
                            break;

                        case StorageType.Double:
                            if (includeRaw) value = ZoneUtil.GetParamAsDisplayDouble(ap);
                            if (includeDisplay) display = ap.AsValueString();
                            try
                            {
                                var dt = ap.Definition?.GetDataType();
                                if (dt != null)
                                {
                                    if (dt.Equals(SpecTypeId.Length)) unit = "mm";
                                    else if (dt.Equals(SpecTypeId.Area)) unit = "m2";
                                    else if (dt.Equals(SpecTypeId.Volume)) unit = "m3";
                                    else if (dt.Equals(SpecTypeId.Angle)) unit = "deg";
                                }
                            }
                            catch { }
                            break;

                        case StorageType.ElementId:
                            value = includeRaw ? (object?)(ap.AsElementId()?.IntValue() ?? 0) : null;
                            display = includeDisplay ? ap.AsValueString() : null;
                            break;

                        default:
                            value = null;
                            display = null;
                            break;
                    }

                    var one = new Dictionary<string, object?>
                    {
                        ["name"] = ap.Definition?.Name,
                        ["storageType"] = ap.StorageType.ToString(),
                        ["isReadOnly"] = ap.IsReadOnly
                    };
                    if (includeUnit) one["unit"] = unit;
                    if (includeDisplay) one["display"] = display;
                    if (includeRaw) one["value"] = value;

                    result.Add(one);
                }

                return new { ok = true, zoneId = zone.Id.IntValue(), totalCount = total, parameters = result };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "get_zone_params 実行中に例外: " + ex.Message };
            }
        }
    }

    // -------------------------- 8) set_zone_param --------------------------
    public class SetZoneParamCommand : IRevitCommandHandler
    {
        public string CommandName => "set_zone_param";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params;
            int? zoneId = p.Value<int?>("zoneId");
            string? zoneUniqueId = p.Value<string?>("zoneUniqueId");
            string? paramName = p.Value<string?>("paramName");
            JToken? valueToken = p["value"];

            if (string.IsNullOrWhiteSpace(paramName))
                return new { ok = false, msg = "paramName を指定してください。" };

            try
            {
                var zone = ZoneUtil.ResolveZone(doc, zoneId, zoneUniqueId);
                if (zone == null) return new { ok = false, msg = "Zone が見つかりません。" };

                using (var t = new Transaction(doc, "Set Zone Parameter"))
                {
                    t.Start();

                    // Name/Number は専用経路
                    if (string.Equals(paramName, "Name", StringComparison.OrdinalIgnoreCase))
                    {
                        var pName = zone.LookupParameter("Name");
                        if (pName == null || pName.IsReadOnly) { t.RollBack(); return new { ok = false, msg = "ゾーン名を設定できません（読み取り専用）。" }; }
                        pName.Set((string?)valueToken?.ToObject<string>() ?? "");
                        t.Commit();
                        return new { ok = true, zoneId = zone.Id.IntValue(), paramName = "Name" };
                    }
                    if (string.Equals(paramName, "Number", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(paramName, "Zone Number", StringComparison.OrdinalIgnoreCase))
                    {
                        var pNum = zone.LookupParameter("Number") ?? zone.LookupParameter("Zone Number");
                        if (pNum == null || pNum.IsReadOnly) { t.RollBack(); return new { ok = false, msg = "ゾーン番号を設定できません（読み取り専用）。" }; }
                        pNum.Set((string?)valueToken?.ToObject<string>() ?? "");
                        t.Commit();
                        return new { ok = true, zoneId = zone.Id.IntValue(), paramName = pNum.Definition?.Name ?? "Number" };
                    }

                    var target = zone.LookupParameter(paramName);
                    if (target == null) { t.RollBack(); return new { ok = false, msg = $"パラメータ '{paramName}' が見つかりません。" }; }

                    var (ok, err) = ZoneUtil.SetParamFromDisplay(target, valueToken);
                    if (!ok) { t.RollBack(); return new { ok = false, msg = err ?? "設定に失敗しました。" }; }

                    t.Commit();
                }

                return new { ok = true, zoneId = zone.Id.IntValue(), paramName, value = valueToken };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "set_zone_param 実行中に例外: " + ex.Message };
            }
        }
    }

    // -------------------------- 9) compute_zone_metrics --------------------------
    public class ComputeZoneMetricsCommand : IRevitCommandHandler
    {
        public string CommandName => "compute_zone_metrics";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params;
            int? zoneId = p.Value<int?>("zoneId");
            string? zoneUniqueId = p.Value<string?>("zoneUniqueId");

            try
            {
                var zone = ZoneUtil.ResolveZone(doc, zoneId, zoneUniqueId);
                if (zone == null) return new { ok = false, msg = "Zone が見つかりません。" };

                double areaM2 = 0.0, volM3 = 0.0;
                int count = 0;
                var perLevel = new Dictionary<int, int>();

                var ids = zone.Spaces;
                if (ids != null)
                {
                    foreach (ElementId sid in ids)
                    {
                        if (!(doc.GetElement(sid) is Autodesk.Revit.DB.Mechanical.Space sp)) continue;
                        count++;
                        areaM2 += ZoneUtil.Ft2ToM2(sp.Area);
                        volM3 += ZoneUtil.Ft3ToM3(sp.Volume);
                        var lid = sp.LevelId?.IntValue() ?? -1;
                        if (lid > 0) perLevel[lid] = perLevel.TryGetValue(lid, out var c) ? c + 1 : 1;
                    }
                }

                var levels = perLevel.Select(kv => new
                {
                    levelId = kv.Key,
                    levelName = (doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(kv.Key)) as Level)?.Name,
                    count = kv.Value
                }).ToList();

                return new
                {
                    ok = true,
                    zoneId = zone.Id.IntValue(),
                    spaceCount = count,
                    areaM2,
                    volumeM3 = volM3,
                    levels
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "compute_zone_metrics 実行中に例外: " + ex.Message };
            }
        }
    }
}


