// ================================================================
// File: Commands/ElementOps/Roof/RoofCommands.cs (UnitHelper対応版)
// 仕様: 入出力の単位変換は UnitHelper に全面委譲（mm/deg ⇄ 内部単位）
// Revit 2023 / .NET Framework 4.8 / C# 8
// 依存: Autodesk.Revit.DB, Autodesk.Revit.UI, Newtonsoft.Json.Linq,
//      RevitMCPAddin.Core (IRevitCommandHandler, RequestCommand, UnitHelper, ResultUtil 等)
// ================================================================
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCP.Abstractions.Models; // Point3D
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Roof
{
    // ------------------------------------------------------------
    // 共通ユーティリティ（UnitHelper 統一）
    // ------------------------------------------------------------
    internal static class RoofUnits
    {
        public static object InputUnits() => new { Length = "mm", Area = "mm2", Volume = "mm3", Angle = "deg" };
        public static object InternalUnits() => new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" };

        // mm Point3D → 内部 XYZ（UnitHelper経由）
        public static XYZ Mm(Point3D p) => new XYZ(
            UnitHelper.ToInternalBySpec(p.X, SpecTypeId.Length),
            UnitHelper.ToInternalBySpec(p.Y, SpecTypeId.Length),
            UnitHelper.ToInternalBySpec(p.Z, SpecTypeId.Length));

        // mm XYZ → 内部 XYZ（XYZ を mm で持っているケース想定）
        public static XYZ Mm(XYZ p) => new XYZ(
            UnitHelper.ToInternalBySpec(p.X, SpecTypeId.Length),
            UnitHelper.ToInternalBySpec(p.Y, SpecTypeId.Length),
            UnitHelper.ToInternalBySpec(p.Z, SpecTypeId.Length));

        // 出力: 内部値 → ユーザー表示（Spec 指定）
        public static object ToUserBySpec(double raw, ForgeTypeId spec) =>
            UnitHelper.ConvertDoubleBySpec(raw, spec);

        // 入力: ユーザー表示 → 内部値（Spec 指定）
        public static double ToInternalBySpec(double v, ForgeTypeId spec) =>
            UnitHelper.ToInternalBySpec(v, spec);

        // 外周ループ（mm座標）→ CurveArray
        public static CurveArray BuildOuterLoop(IList<Point3D> pts)
        {
            var ca = new CurveArray();
            for (int i = 0; i < pts.Count; i++)
            {
                var a = Mm(pts[i]);
                var b = Mm(pts[(i + 1) % pts.Count]);
                ca.Append(Line.CreateBound(a, b));
            }
            return ca;
        }

        public static bool TryGetLevel(Autodesk.Revit.DB.Document doc, JObject p, out Level level)
        {
            level = null;
            int levelId = p.Value<int?>("levelId") ?? 0;
            string levelName = p.Value<string>("levelName");
            if (levelId > 0) level = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(levelId)) as Level;
            if (level == null && !string.IsNullOrWhiteSpace(levelName))
            {
                level = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).Cast<Level>()
                    .FirstOrDefault(l => string.Equals(l.Name, levelName, StringComparison.OrdinalIgnoreCase));
            }
            return level != null;
        }

        public static bool TryGetRoofType(Autodesk.Revit.DB.Document doc, JObject p, out RoofType rt)
        {
            rt = null;
            int typeId = p.Value<int?>("roofTypeId") ?? p.Value<int?>("typeId") ?? 0;
            string typeName = p.Value<string>("typeName");
            string familyName = p.Value<string>("familyName");
            if (typeId > 0) rt = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(typeId)) as RoofType;
            if (rt == null && !string.IsNullOrWhiteSpace(typeName))
            {
                var q = new FilteredElementCollector(doc).OfClass(typeof(RoofType)).OfCategory(BuiltInCategory.OST_Roofs).Cast<RoofType>()
                    .Where(t => string.Equals(t.Name ?? "", typeName, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(familyName))
                    q = q.Where(t => string.Equals(t.FamilyName ?? "", familyName, StringComparison.OrdinalIgnoreCase));
                rt = q.OrderBy(t => t.FamilyName ?? "").ThenBy(t => t.Name ?? "").FirstOrDefault();
            }
            return rt != null;
        }

        public static Element ResolveElement(Document doc, JObject p)
        {
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) return doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
            if (!string.IsNullOrWhiteSpace(uid)) return doc.GetElement(uid);
            return null;
        }

        public static XYZ ReadOffset(JObject p)
        {
            var off = p.Value<JObject>("offset");
            if (off != null)
            {
                return new XYZ(
                    UnitHelper.ToInternalBySpec(off.Value<double>("x"), SpecTypeId.Length),
                    UnitHelper.ToInternalBySpec(off.Value<double>("y"), SpecTypeId.Length),
                    UnitHelper.ToInternalBySpec(off.Value<double>("z"), SpecTypeId.Length));
            }
            // dx/dy/dz フォールバック
            double dx = p.Value<double?>("dx") ?? 0;
            double dy = p.Value<double?>("dy") ?? 0;
            double dz = p.Value<double?>("dz") ?? 0;
            return new XYZ(
                UnitHelper.ToInternalBySpec(dx, SpecTypeId.Length),
                UnitHelper.ToInternalBySpec(dy, SpecTypeId.Length),
                UnitHelper.ToInternalBySpec(dz, SpecTypeId.Length));
        }
    }

    // ------------------------------------------------------------
    // 1) create_roof（外周ポリゴン必須・mm入力）
    //    入力: boundary または loops: [{ points: [Point3D...] }]
    // ------------------------------------------------------------
    public class CreateRoofCommand : IRevitCommandHandler
    {
        public string CommandName => "create_roof";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            // boundary/loops どちらでも可（外周のみ使用）
            var loopsTok = (p["loops"] ?? p["boundary"]) as JArray;
            if (loopsTok == null || loopsTok.Count == 0) return ResultUtil.Err("boundary/loops が必要です。");

            var firstPoints = loopsTok[0]?["points"]?.ToObject<List<Point3D>>();
            if (firstPoints == null || firstPoints.Count < 3) return ResultUtil.Err("boundary の頂点は 3 点以上が必要です。");

            // Level / RoofType 取得（両対応）
            if (!RoofUnits.TryGetLevel(doc, p, out var level)) return ResultUtil.Err("Level が見つかりません（levelId/levelName）。");
            if (!RoofUnits.TryGetRoofType(doc, p, out var rtype)) return ResultUtil.Err("RoofType が見つかりません（roofTypeId/typeName）。");

            // 外周曲線
            var profile = RoofUnits.BuildOuterLoop(firstPoints);

            FootPrintRoof roof;
            using (var tx = new Transaction(doc, "Create Roof"))
            {
                tx.Start();
                ModelCurveArray mapping;
                roof = doc.Create.NewFootPrintRoof(profile, level, rtype, out mapping);
                tx.Commit();
            }

            return new
            {
                ok = true,
                elementId = roof.Id.IntValue(),
                uniqueId = roof.UniqueId,
                typeId = roof.RoofType?.Id.IntValue(),
                levelId = roof.LevelId?.IntValue(),
                inputUnits = RoofUnits.InputUnits(),
                internalUnits = RoofUnits.InternalUnits()
            };
        }
    }

    public class DeleteRoofCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_roof";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            var p = (JObject)cmd.Params;

            // 対象解決（elementId / uniqueId 両対応）
            Element target = RoofUnits.ResolveElement(doc, p);
            if (target == null) return ResultUtil.Err("Roof 要素が見つかりません（elementId/uniqueId）。");

            // Roof かどうか判定（FootPrintRoof または Roof カテゴリ）
            bool isRoof = target is FootPrintRoof
                       || (target.Category != null && target.Category.Id.IntValue() == (int)BuiltInCategory.OST_Roofs);
            if (!isRoof) return ResultUtil.Err("対象は Roof ではありません。");

            int targetId = target.Id.IntValue();
            string targetUid = target.UniqueId;

            ICollection<ElementId> deleted;
            using (var tx = new Transaction(doc, "Delete Roof"))
            {
                tx.Start();
                try
                {
                    deleted = doc.Delete(target.Id); // 関連要素（スケッチ等）も返る
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return ResultUtil.Err($"削除に失敗: {ex.Message}");
                }
                tx.Commit();
            }

            var deletedIds = deleted != null ? deleted.Select(x => x.IntValue()).ToList() : new List<int>();

            return new
            {
                ok = true,
                elementId = targetId,
                uniqueId = targetUid,
                deletedCount = deletedIds.Count,
                deletedElementIds = deletedIds
            };
        }
    }

    // ------------------------------------------------------------
    // 2) move_roof（mm オフセット / elementId or uniqueId）
    // ------------------------------------------------------------
    public class MoveRoofCommand : IRevitCommandHandler
    {
        public string CommandName => "move_roof";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            var el = RoofUnits.ResolveElement(doc, p);
            if (el == null) return ResultUtil.Err("Roof 要素が見つかりません（elementId/uniqueId）。");

            var offset = RoofUnits.ReadOffset(p);
            using (var tx = new Transaction(doc, "Move Roof"))
            {
                tx.Start();
                ElementTransformUtils.MoveElement(doc, el.Id, offset);
                tx.Commit();
            }

            return new
            {
                ok = true,
                elementId = el.Id.IntValue(),
                uniqueId = el.UniqueId,
                inputUnits = new { Length = "mm" },
                internalUnits = new { Length = "ft" }
            };
        }
    }

    // ------------------------------------------------------------
    // 3) update_roof_boundary（外周再作成：旧屋根を削除→新規作成）
    //    ※簡易・確実を優先（開口や勾配線編集はスコープ外）
    // ------------------------------------------------------------
    public class UpdateRoofBoundaryCommand : IRevitCommandHandler
    {
        public string CommandName => "update_roof_boundary";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            // 旧屋根
            var old = RoofUnits.ResolveElement(doc, p) as FootPrintRoof;
            if (old == null) return ResultUtil.Err("Roof が見つかりません（elementId/uniqueId）。");

            // 新境界
            var loopsTok = (p["loops"] ?? p["boundary"]) as JArray;
            if (loopsTok == null || loopsTok.Count == 0) return ResultUtil.Err("boundary/loops が必要です。");
            var points = loopsTok[0]?["points"]?.ToObject<List<Point3D>>();
            if (points == null || points.Count < 3) return ResultUtil.Err("boundary の頂点は 3 点以上が必要です。");

            var level = doc.GetElement(old.LevelId) as Level;
            var rtype = old.RoofType as RoofType;
            var profile = RoofUnits.BuildOuterLoop(points);

            FootPrintRoof newRoof;
            using (var tx = new Transaction(doc, "Update Roof Boundary"))
            {
                tx.Start();
                doc.Delete(old.Id);
                ModelCurveArray mapping;
                newRoof = doc.Create.NewFootPrintRoof(profile, level, rtype, out mapping);
                tx.Commit();
            }

            return new
            {
                ok = true,
                elementId = newRoof.Id.IntValue(),
                uniqueId = newRoof.UniqueId,
                typeId = newRoof.RoofType?.Id.IntValue()
            };
        }
    }

    // ------------------------------------------------------------
    // 4) set_roof_parameter（mm/deg 入力→内部変換／elementId or uniqueId）
    // ------------------------------------------------------------
    public class SetRoofParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "set_roof_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            var el = RoofUnits.ResolveElement(doc, p);
            if (el == null) return ResultUtil.Err("Roof 要素が見つかりません（elementId/uniqueId）。");

            var name = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(name) && p["builtInName"] == null && p["builtInId"] == null && p["guid"] == null)
                return ResultUtil.Err("paramName または builtInName/builtInId/guid が必要です。");
            if (!p.TryGetValue("value", out var vtok)) return ResultUtil.Err("value が必要です。");
            var pa = ParamResolver.ResolveByPayload(el, p, out var resolvedBy);
            if (pa == null) return ResultUtil.Err($"Parameter not found (name/builtIn/guid).");
            if (pa.IsReadOnly) return ResultUtil.Err($"Parameter '{name}' は読み取り専用です。");

            using (var tx = new Transaction(doc, $"Set Roof Parameter {name}"))
            {
                tx.Start();
                try
                {
                    switch (pa.StorageType)
                    {
                        case StorageType.Double:
                            {
                                ForgeTypeId spec = null;
                                try { spec = pa.Definition?.GetDataType(); } catch { spec = null; }
                                var user = vtok.Value<double>();
                                var internalVal = RoofUnits.ToInternalBySpec(user, spec ?? SpecTypeId.Length);
                                pa.Set(internalVal);
                                break;
                            }
                        case StorageType.Integer: pa.Set(vtok.Value<int>()); break;
                        case StorageType.String: pa.Set(vtok.Value<string>() ?? string.Empty); break;
                        case StorageType.ElementId: pa.Set(Autodesk.Revit.DB.ElementIdCompat.From(vtok.Value<int>())); break;
                        default:
                            tx.RollBack();
                            return ResultUtil.Err($"Unsupported StorageType: {pa.StorageType}");
                    }
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return ResultUtil.Err($"Set failed: {ex.Message}");
                }
                tx.Commit();
            }

            return new { ok = true, elementId = el.Id.IntValue(), uniqueId = el.UniqueId };
        }
    }

    // ------------------------------------------------------------
    // 5) set_roof_type_parameter（タイプパラメータのmm/deg入力→内部変換）
    //    指定: typeId / typeName(+familyName)
    // ------------------------------------------------------------
    public class SetRoofTypeParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "set_roof_type_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            if (!RoofUnits.TryGetRoofType(doc, p, out var rt))
                return ResultUtil.Err("RoofType が見つかりません（typeId/typeName）。");

            var name = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(name) && p["builtInName"] == null && p["builtInId"] == null && p["guid"] == null)
                return ResultUtil.Err("paramName または builtInName/builtInId/guid が必要です。");
            if (!p.TryGetValue("value", out var vtok)) return ResultUtil.Err("value が必要です。");
            var pa = ParamResolver.ResolveByPayload(rt, p, out var resolvedBy2);
            if (pa == null) return ResultUtil.Err($"Parameter '{name}' not found on type.");
            if (pa.IsReadOnly) return ResultUtil.Err($"Parameter '{name}' は読み取り専用です。");

            using (var tx = new Transaction(doc, $"Set RoofType Parameter {name}"))
            {
                tx.Start();
                try
                {
                    switch (pa.StorageType)
                    {
                        case StorageType.Double:
                            {
                                ForgeTypeId spec = null;
                                try { spec = pa.Definition?.GetDataType(); } catch { spec = null; }
                                var user = vtok.Value<double>();
                                pa.Set(RoofUnits.ToInternalBySpec(user, spec ?? SpecTypeId.Length));
                                break;
                            }
                        case StorageType.Integer: pa.Set(vtok.Value<int>()); break;
                        case StorageType.String: pa.Set(vtok.Value<string>() ?? string.Empty); break;
                        case StorageType.ElementId: pa.Set(Autodesk.Revit.DB.ElementIdCompat.From(vtok.Value<int>())); break;
                        default:
                            tx.RollBack();
                            return ResultUtil.Err($"Unsupported StorageType: {pa.StorageType}");
                    }
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return ResultUtil.Err($"Set failed: {ex.Message}");
                }
                tx.Commit();
            }

            return new { ok = true, typeId = rt.Id.IntValue(), uniqueId = rt.UniqueId };
        }
    }

    // ------------------------------------------------------------
    // 6) get_roof_slope（角度は deg 出力 / elementId or uniqueId）
    // ------------------------------------------------------------
    public class GetRoofSlopeCommand : IRevitCommandHandler
    {
        public string CommandName => "get_roof_slope";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            var el = RoofUnits.ResolveElement(doc, p);
            if (el == null) return ResultUtil.Err("Roof 要素が見つかりません（elementId/uniqueId）。");

            var pa = el.LookupParameter("Slope");
            if (pa == null || pa.StorageType != StorageType.Double)
                return ResultUtil.Err("Slope パラメータが見つからないか非 Double です。");

            ForgeTypeId spec = null;
            try { spec = pa.Definition?.GetDataType(); } catch { spec = null; }
            var valUser = RoofUnits.ToUserBySpec(pa.AsDouble(), spec ?? SpecTypeId.Angle); // deg 期待

            return new
            {
                ok = true,
                elementId = el.Id.IntValue(),
                uniqueId = el.UniqueId,
                slopeDeg = valUser, // 仕様上 deg 返却
                inputUnits = new { Angle = "deg" },
                internalUnits = new { Angle = "rad" }
            };
        }
    }

    // ------------------------------------------------------------
    // 7) set_roof_slope（角度 deg 入力 → 内部に変換して設定）
    // ------------------------------------------------------------
    public class SetRoofSlopeCommand : IRevitCommandHandler
    {
        public string CommandName => "set_roof_slope";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            var el = RoofUnits.ResolveElement(doc, p);
            if (el == null) return ResultUtil.Err("Roof 要素が見つかりません（elementId/uniqueId）。");

            var pa = el.LookupParameter("Slope");
            if (pa == null || pa.StorageType != StorageType.Double)
                return ResultUtil.Err("Slope パラメータが見つからないか非 Double です。");

            double slopeDeg = p.Value<double>("slope");
            ForgeTypeId spec = null; try { spec = pa.Definition?.GetDataType(); } catch { spec = null; }
            double slopeInternal = RoofUnits.ToInternalBySpec(slopeDeg, spec ?? SpecTypeId.Angle);

            using (var tx = new Transaction(doc, "Set Roof Slope"))
            {
                tx.Start();
                pa.Set(slopeInternal);
                tx.Commit();
            }
            return new { ok = true, elementId = el.Id.IntValue(), uniqueId = el.UniqueId };
        }
    }

    // ------------------------------------------------------------
    // 8) change_roof_type（タイプ変更）
    // ------------------------------------------------------------
    public class ChangeRoofTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "change_roof_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            var p = (JObject)cmd.Params;

            // 対象
            var target = RoofUnits.ResolveElement(doc, p);
            if (target == null) return ResultUtil.Err("Roof 要素が見つかりません（elementId/uniqueId）。");

            bool isRoof = target is FootPrintRoof
                          || (target.Category != null && target.Category.Id.IntValue() == (int)BuiltInCategory.OST_Roofs);
            if (!isRoof) return ResultUtil.Err("対象は Roof ではありません。");

            // 新タイプ解決
            RoofType newType = null;
            int newTypeId = p.Value<int?>("newTypeId")
                         ?? p.Value<int?>("typeId")
                         ?? p.Value<int?>("roofTypeId")
                         ?? 0;

            if (newTypeId > 0)
            {
                newType = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(newTypeId)) as RoofType;
                if (newType == null) return ResultUtil.Err($"RoofType(typeId={newTypeId}) が見つかりません。");
            }
            else
            {
                string typeName = p.Value<string>("typeName");
                string familyName = p.Value<string>("familyName");
                if (!string.IsNullOrWhiteSpace(typeName))
                {
                    var q = new FilteredElementCollector(doc)
                        .OfClass(typeof(RoofType))
                        .OfCategory(BuiltInCategory.OST_Roofs)
                        .Cast<RoofType>()
                        .Where(t => string.Equals(t.Name ?? "", typeName, StringComparison.OrdinalIgnoreCase));

                    if (!string.IsNullOrWhiteSpace(familyName))
                        q = q.Where(t => string.Equals(t.FamilyName ?? "", familyName, StringComparison.OrdinalIgnoreCase));

                    newType = q.OrderBy(t => t.FamilyName ?? "")
                               .ThenBy(t => t.Name ?? "")
                               .FirstOrDefault();
                }

                if (newType == null)
                    return ResultUtil.Err("新しい RoofType が見つかりません（newTypeId / typeName(+familyName) を確認）。");

                newTypeId = newType.Id.IntValue();
            }

            int oldTypeId = target.GetTypeId()?.IntValue() ?? -1;

            using (var tx = new Transaction(doc, "Change Roof Type"))
            {
                tx.Start();
                try
                {
                    target.ChangeTypeId(newType.Id);
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return ResultUtil.Err($"タイプ変更に失敗: {ex.Message}");
                }
                tx.Commit();
            }

            return new
            {
                ok = true,
                elementId = target.Id.IntValue(),
                uniqueId = target.UniqueId,
                oldTypeId = oldTypeId,
                typeId = target.GetTypeId()?.IntValue()
            };
        }
    }

    // ------------------------------------------------------------
    // X) get_roofs（filters / paging / namesOnly / idsOnly / summaryOnly）
    //     - _shape: { idsOnly?:bool, page?:{ limit?:int, skip?:int } }
    //     - legacy: skip/count/namesOnly を後方互換で維持
    //     - includeLocation: 位置(BB中心)の算出を任意化（既定: true）
    // ------------------------------------------------------------
    public class GetRoofsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_roofs";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());

            // legacy paging (backward compatible)
            int legacySkip = p.Value<int?>("skip") ?? 0;
            int legacyCount = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            // shape/paging
            var shape = p["_shape"] as JObject;
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
            var page = shape?["page"] as JObject;
            int limit = Math.Max(0, page?.Value<int?>("limit") ?? legacyCount);
            int skip = Math.Max(0, page?.Value<int?>("skip") ?? page?.Value<int?>("offset") ?? legacySkip);
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;
            bool includeLocation = p.Value<bool?>("includeLocation") ?? true;

            // filters
            int targetEid = p.Value<int?>("elementId") ?? 0;
            string targetUid = p.Value<string>("uniqueId");
            int typeId = p.Value<int?>("typeId") ?? p.Value<int?>("roofTypeId") ?? 0;
            string typeName = p.Value<string>("typeName");
            string familyName = p.Value<string>("familyName");
            int levelId = p.Value<int?>("levelId") ?? 0;
            string levelName = p.Value<string>("levelName");
            string nameContains = p.Value<string>("nameContains");

            var collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Roofs)
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .OfType<RoofBase>();

            IEnumerable<RoofBase> q = collector;

            if (targetEid > 0 || !string.IsNullOrWhiteSpace(targetUid))
            {
                RoofBase target = null;
                if (targetEid > 0) target = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(targetEid)) as RoofBase;
                else target = doc.GetElement(targetUid) as RoofBase;
                q = target == null ? Enumerable.Empty<RoofBase>() : new[] { target };
            }

            if (typeId > 0) q = q.Where(r => r.GetTypeId().IntValue() == typeId);
            if (!string.IsNullOrWhiteSpace(typeName) || !string.IsNullOrWhiteSpace(familyName))
            {
                q = q.Where(r =>
                {
                    var rt = doc.GetElement(r.GetTypeId()) as RoofType;
                    if (rt == null) return false;
                    if (!string.IsNullOrWhiteSpace(typeName) && !string.Equals(rt.Name ?? string.Empty, typeName, StringComparison.OrdinalIgnoreCase)) return false;
                    if (!string.IsNullOrWhiteSpace(familyName) && !string.Equals(rt.FamilyName ?? string.Empty, familyName, StringComparison.OrdinalIgnoreCase)) return false;
                    return true;
                });
            }

            if (levelId > 0) q = q.Where(r => r.LevelId.IntValue() == levelId);
            if (!string.IsNullOrWhiteSpace(levelName))
            {
                q = q.Where(r =>
                {
                    var lv = doc.GetElement(r.LevelId) as Level;
                    return lv != null && string.Equals(lv.Name ?? string.Empty, levelName, StringComparison.OrdinalIgnoreCase);
                });
            }

            if (!string.IsNullOrWhiteSpace(nameContains))
            {
                q = q.Where(r =>
                {
                    var n = r.Name ?? string.Empty;
                    if (n.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    var rt = doc.GetElement(r.GetTypeId()) as RoofType;
                    return (rt?.Name ?? string.Empty).IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0;
                });
            }

            // materialize and count
            var filtered = q.ToList();
            int totalCount = filtered.Count;
            if (summaryOnly || limit == 0)
                return new { ok = true, totalCount, inputUnits = new { Length = "mm" }, internalUnits = new { Length = "ft" } };

            // precompute type/family names
            var typeNameMap = new Dictionary<int, string>();
            var familyNameMap = new Dictionary<int, string>();
            foreach (var tid in filtered.Select(r => r.GetTypeId().IntValue()).Distinct())
            {
                var rt = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(tid)) as RoofType;
                typeNameMap[tid] = rt?.Name ?? string.Empty;
                familyNameMap[tid] = rt?.FamilyName ?? string.Empty;
            }

            var ordered = filtered
                .OrderBy(r => typeNameMap.TryGetValue(r.GetTypeId().IntValue(), out var tn) ? tn : string.Empty)
                .ThenBy(r => r.Id.IntValue())
                .ToList();

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(limit).Select(r =>
                {
                    var n = r.Name ?? string.Empty;
                    if (!string.IsNullOrEmpty(n)) return n;
                    var tid2 = r.GetTypeId().IntValue();
                    return typeNameMap.TryGetValue(tid2, out var tn2) ? tn2 : string.Empty;
                }).ToList();

                return new { ok = true, totalCount, names, inputUnits = new { Length = "mm" }, internalUnits = new { Length = "ft" } };
            }

            // paging and idsOnly
            IEnumerable<RoofBase> paged = ordered;
            if (skip > 0) paged = paged.Skip(skip);
            if (limit > 0 && limit != int.MaxValue) paged = paged.Take(limit);

            if (idsOnly)
            {
                var ids = paged.Select(r => r.Id.IntValue()).ToList();
                return new { ok = true, totalCount, elementIds = ids };
            }

            var items = paged.Select(r =>
            {
                object location = null;
                if (includeLocation)
                {
                    try
                    {
                        var bb = r.get_BoundingBox(null);
                        if (bb != null)
                        {
                            var center = (bb.Min + bb.Max) / 2.0;
                            var mm = UnitHelper.XyzToMm(center);
                            location = new { x = Math.Round(mm.x, 3), y = Math.Round(mm.y, 3), z = Math.Round(mm.z, 3) };
                        }
                    }
                    catch { }
                }

                var lv = doc.GetElement(r.LevelId) as Level;
                var tid = r.GetTypeId().IntValue();
                var tName = typeNameMap.TryGetValue(tid, out var tnm) ? tnm : string.Empty;
                var fName = familyNameMap.TryGetValue(tid, out var fnm) ? fnm : string.Empty;

                return new
                {
                    elementId = r.Id.IntValue(),
                    uniqueId = r.UniqueId,
                    typeId = tid,
                    typeName = tName,
                    familyName = fName,
                    levelId = r.LevelId.IntValue(),
                    levelName = lv?.Name ?? string.Empty,
                    location
                };
            }).ToList();

            return new { ok = true, totalCount, roofs = items, inputUnits = new { Length = "mm" }, internalUnits = new { Length = "ft" } };
        }
    }
}


