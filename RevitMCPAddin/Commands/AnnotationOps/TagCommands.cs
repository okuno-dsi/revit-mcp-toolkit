// ================================================================
// File: Commands/AnnotationOps/TagCommands.cs (InputPointReader対応版)
// Revit 2023 / .NET Framework 4.8 / C# 8
// 改変点：位置・オフセット読取を InputPointReader に統一（mm基準）
// それ以外のロジックは旧版と等価
// ================================================================
#nullable enable
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RevitMCPAddin.Commands.AnnotationOps
{
    internal static class TagHelpers
    {
        public static object UnitsIn() => UnitHelper.DefaultUnitsMeta();
        public static object UnitsInt() => new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" };

        public static View ResolveView(Document doc, JObject p)
        {
            if (p.TryGetValue("viewId", out var vt))
            {
                var v = doc.GetElement(new ElementId(vt.Value<int>())) as View;
                if (v != null) return v;
            }
            var vu = p.Value<string>("viewUniqueId");
            if (!string.IsNullOrWhiteSpace(vu))
            {
                var v = doc.GetElement(vu) as View;
                if (v != null) return v;
            }
            return null;
        }

        public static bool ViewAllowsAnnotation(View v)
        {
            if (v == null) return false;
            if (v.IsTemplate) return false;
            return true;
        }

        public static Element ResolveElement(Document doc, JObject p)
        {
            if (p.TryGetValue("hostElementId", out var et))
            {
                var e = doc.GetElement(new ElementId(et.Value<int>()));
                if (e != null) return e;
            }
            var uid = p.Value<string>("uniqueId");
            if (!string.IsNullOrWhiteSpace(uid))
            {
                var e = doc.GetElement(uid);
                if (e != null) return e;
            }
            return null;
        }

        public static FamilySymbol ResolveTagType(Document doc, JObject p)
        {
            if (p.TryGetValue("typeId", out var tt))
            {
                var fs = doc.GetElement(new ElementId(tt.Value<int>())) as FamilySymbol;
                if (fs != null) return fs;
            }

            string typeName = p.Value<string>("typeName");
            string familyName = p.Value<string>("familyName");
            string categoryName = p.Value<string>("categoryName"); // e.g. "Door Tags"

            var col = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>();

            if (!string.IsNullOrWhiteSpace(categoryName))
                col = col.Where(fs => string.Equals(fs.Category?.Name ?? "", categoryName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(familyName))
                col = col.Where(fs => string.Equals(fs.Family?.Name ?? "", familyName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(typeName))
                col = col.Where(fs => string.Equals(fs.Name ?? "", typeName, StringComparison.OrdinalIgnoreCase));

            return col.FirstOrDefault();
        }
    }

    public class GetTagSymbolsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_tag_symbols";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var shape = p["_shape"] as JObject;
            int skip = Math.Max(0, (shape?["page"] as JObject)?.Value<int?>("skip") ?? (shape?["page"] as JObject)?.Value<int?>("offset") ?? p.Value<int?>("skip") ?? 0);
            int count = Math.Max(0, (shape?["page"] as JObject)?.Value<int?>("limit") ?? p.Value<int?>("count") ?? int.MaxValue);
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;

            string category = p.Value<string>("category");
            var categoryIds = (p["categoryIds"] as JArray)?.Values<int>().ToHashSet() ?? new HashSet<int>();
            var categoryNames = (p["categoryNames"] as JArray)?.Values<string>().ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string familyName = p.Value<string>("familyName");
            string nameContains = p.Value<string>("nameContains");

            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs =>
                {
                    var cat = fs.Category;
                    if (cat == null) return false;

                    bool ok = true;
                    if (!string.IsNullOrWhiteSpace(category))
                        ok &= cat.Name.EndsWith(category, StringComparison.OrdinalIgnoreCase) || string.Equals(cat.Name, category, StringComparison.OrdinalIgnoreCase);
                    if (categoryIds.Count > 0)
                        ok &= categoryIds.Contains(cat.Id.IntegerValue);
                    if (categoryNames.Count > 0)
                        ok &= categoryNames.Contains(cat.Name);

                    return ok;
                })
                .ToList();

            if (!string.IsNullOrWhiteSpace(familyName))
                all = all.Where(fs => string.Equals(fs.Family?.Name ?? "", familyName, StringComparison.OrdinalIgnoreCase)).ToList();

            if (!string.IsNullOrWhiteSpace(nameContains))
                all = all.Where(fs => (fs.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            var ordered = all
                .Select(fs => new { fs, fam = fs.Family?.Name ?? "", cat = fs.Category?.Name ?? "", name = fs.Name ?? "", id = fs.Id.IntegerValue })
                .OrderBy(x => x.cat).ThenBy(x => x.fam).ThenBy(x => x.name).ThenBy(x => x.id)
                .Select(x => x.fs).ToList();

            int totalCount = ordered.Count; 

            if (summaryOnly || count == 0) return new { ok = true, totalCount }; 

            if (idsOnly)
            {
                var ids = ordered.Skip(skip).Take(count).Select(fs => fs.Id.IntegerValue).ToList();
                return new { ok = true, totalCount, typeIds = ids };
            }

            if (namesOnly) 
            { 
                var names = ordered.Skip(skip).Take(count).Select(fs => fs.Name ?? "").ToList(); 
                return new { ok = true, totalCount, names }; 
            } 

            var list = ordered.Skip(skip).Take(count).Select(fs => new
            {
                typeId = fs.Id.IntegerValue,
                uniqueId = fs.UniqueId,
                typeName = fs.Name ?? "",
                familyName = fs.Family?.Name ?? "",
                categoryId = fs.Category?.Id.IntegerValue,
                categoryName = fs.Category?.Name ?? ""
            }).ToList();

            return new { ok = true, totalCount, types = list };
        }
    }

    public class GetTagsInViewCommand : IRevitCommandHandler 
    { 
        public string CommandName => "get_tags_in_view"; 
 
        public object Execute(UIApplication uiapp, RequestCommand cmd) 
        { 
            var p = (JObject)cmd.Params; 
            var doc = uiapp?.ActiveUIDocument?.Document; 
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" }; 
 
            var view = TagHelpers.ResolveView(doc, p); 
            if (view == null) return new { ok = false, msg = "View が見つかりません（viewId/viewUniqueId）。" }; 
            if (!TagHelpers.ViewAllowsAnnotation(view)) return new { ok = false, msg = "このビューではタグ情報の取得/作成が制限される場合があります。" }; 
 
            // shape/paging/summary
            var shape = p["_shape"] as JObject; 
            int skip = Math.Max(0, (shape?["page"] as JObject)?.Value<int?>("skip") ?? (shape?["page"] as JObject)?.Value<int?>("offset") ?? p.Value<int?>("skip") ?? 0); 
            int count = Math.Max(0, (shape?["page"] as JObject)?.Value<int?>("limit") ?? p.Value<int?>("count") ?? int.MaxValue); 
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false; 
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false; 
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false; 
 
            string categoryName = p.Value<string>("categoryName"); 
            string nameContains = p.Value<string>("nameContains"); 
            var categoryIds = (p["categoryIds"] as JArray)?.Values<int>()?.ToHashSet() ?? new HashSet<int>(); 
            var tagTypeIds = (p["tagTypeIds"] as JArray)?.Values<int>()?.ToHashSet() ?? new HashSet<int>(); 
 
            var all = new FilteredElementCollector(doc, view.Id) 
                .OfClass(typeof(IndependentTag)) 
                .Cast<IndependentTag>() 
                .ToList(); 
 
            if (!string.IsNullOrWhiteSpace(categoryName)) 
                all = all.Where(t => string.Equals(t.Category?.Name ?? "", categoryName, StringComparison.OrdinalIgnoreCase)).ToList(); 
            if (categoryIds.Count > 0) 
                all = all.Where(t => t.Category != null && categoryIds.Contains(t.Category.Id.IntegerValue)).ToList(); 
            if (tagTypeIds.Count > 0) 
                all = all.Where(t => tagTypeIds.Contains(t.GetTypeId().IntegerValue)).ToList(); 
 
            if (!string.IsNullOrWhiteSpace(nameContains)) 
                all = all.Where(t => (t.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0).ToList(); 
 
            var ordered = all.OrderBy(t => t.Category?.Name ?? "") 
                             .ThenBy(t => t.Id.IntegerValue).ToList(); 
 
            int totalCount = ordered.Count; 
            if (summaryOnly || count == 0) 
                return new { ok = true, totalCount, inputUnits = TagHelpers.UnitsIn(), internalUnits = TagHelpers.UnitsInt() }; 
 
            if (namesOnly) 
            { 
                var names = ordered.Skip(skip).Take(count).Select(t => t.Name ?? "").ToList(); 
                return new { ok = true, totalCount, names, inputUnits = TagHelpers.UnitsIn(), internalUnits = TagHelpers.UnitsInt() }; 
            } 
 
            if (idsOnly)
            {
                var tagIds = ordered.Skip(skip).Take(count).Select(t => t.Id.IntegerValue).ToList();
                return new { ok = true, totalCount, tagIds, inputUnits = TagHelpers.UnitsIn(), internalUnits = TagHelpers.UnitsInt() };
            }

            var tags = ordered.Skip(skip).Take(count).Select(tag => 
            { 
                XYZ head; 
                try { head = tag.TagHeadPosition; } catch { head = (tag.Location as LocationPoint)?.Point ?? XYZ.Zero; } 
 
                var hostIds = tag.GetTaggedLocalElementIds(); 
                int hostId = (hostIds != null && hostIds.Count > 0) ? hostIds.First().IntegerValue : 0;

                return new
                {
                    tagId = tag.Id.IntegerValue,
                    uniqueId = tag.UniqueId,
                    tagTypeId = tag.GetTypeId().IntegerValue,
                    categoryId = tag.Category?.Id.IntegerValue,
                    categoryName = tag.Category?.Name ?? "",
                    hostElementId = hostId,
                    hasLeader = tag.HasLeader,
                    orientation = tag.TagOrientation.ToString(),
                    location = new
                    {
                        x = Math.Round(UnitHelper.FtToMm(head.X), 3),
                        y = Math.Round(UnitHelper.FtToMm(head.Y), 3),
                        z = Math.Round(UnitHelper.FtToMm(head.Z), 3)
                    }
                };
            }).ToList();

            return new { ok = true, totalCount, tags, inputUnits = TagHelpers.UnitsIn(), internalUnits = TagHelpers.UnitsInt() };
        }
    }

    public class CreateTagCommand : IRevitCommandHandler
    {
        public string CommandName => "create_tag";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var view = TagHelpers.ResolveView(doc, p);
            if (view == null) return new { ok = false, msg = "View が見つかりません（viewId/viewUniqueId）。" };
            if (!TagHelpers.ViewAllowsAnnotation(view)) return new { ok = false, msg = "このビューではタグを作成できません。" };

            var host = TagHelpers.ResolveElement(doc, p);
            if (host == null) return new { ok = false, msg = "Host element が見つかりません（hostElementId/uniqueId）。" };

            var symbol = TagHelpers.ResolveTagType(doc, p);
            if (symbol == null) return new { ok = false, msg = "Tag type を解決できません（typeId または typeName(+familyName[+categoryName])）。" };

            // 位置 (mm) は InputPointReader
            if (!InputPointReader.TryReadPointMm(p, out var headMm, "location"))
                return new { ok = false, msg = "location {x,y,z} (mm) が必要です。" };
            var head = new XYZ(UnitHelper.MmToFt(headMm.X), UnitHelper.MmToFt(headMm.Y), UnitHelper.MmToFt(headMm.Z));

            bool addLeader = p.Value<bool?>("addLeader") ?? true;
            string oriStr = (p.Value<string>("orientation") ?? "Horizontal");
            TagOrientation orientation = oriStr.Equals("Vertical", StringComparison.OrdinalIgnoreCase) ? TagOrientation.Vertical : TagOrientation.Horizontal;

            IndependentTag tag = null;
            using (var tx = new Transaction(doc, "Create Tag"))
            {
                tx.Start();
                try
                {
                    if (!symbol.IsActive) symbol.Activate();
                    var reference = new Reference(host);
                    tag = IndependentTag.Create(doc, symbol.Id, view.Id, reference, addLeader, orientation, head);

                    // Leader elbow / end も InputPointReader で任意対応
                    if (InputPointReader.TryReadPointMm(p, out var elbowMm, "leaderElbow", "leaderEnd"))
                    {
                        var elbow = new XYZ(UnitHelper.MmToFt(elbowMm.X), UnitHelper.MmToFt(elbowMm.Y), UnitHelper.MmToFt(elbowMm.Z));
                        var propElbow = typeof(IndependentTag).GetProperty("LeaderElbow", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (propElbow != null && propElbow.CanWrite)
                        {
                            try { propElbow.SetValue(tag, elbow, null); } catch { /* ignore */ }
                        }
                        else
                        {
                            var propEnd = typeof(IndependentTag).GetProperty("LeaderEnd", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (propEnd != null && propEnd.CanWrite)
                            {
                                try { propEnd.SetValue(tag, elbow, null); } catch { /* ignore */ }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = $"タグ作成に失敗: {ex.Message}" };
                }
                tx.Commit();
            }

            return new
            {
                ok = true,
                tagId = tag.Id.IntegerValue,
                uniqueId = tag.UniqueId,
                typeId = symbol.Id.IntegerValue,
                inputUnits = TagHelpers.UnitsIn(),
                internalUnits = TagHelpers.UnitsInt()
            };
        }
    }

    public class MoveTagCommand : IRevitCommandHandler
    {
        public string CommandName => "move_tag";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            Element tag = null;
            int tagId = p.Value<int?>("tagId") ?? 0;
            string tagUid = p.Value<string>("uniqueId");
            if (tagId > 0) tag = doc.GetElement(new ElementId(tagId));
            else if (!string.IsNullOrWhiteSpace(tagUid)) tag = doc.GetElement(tagUid);
            if (tag == null) return new { ok = false, msg = "Tag が見つかりません（tagId/uniqueId）。" };

            // offsetMm / offset / dx,dy,dz のいずれでもOK
            if (!InputPointReader.TryReadOffsetMm(p, out var deltaMm))
                return new { ok = false, msg = "offsetMm{ x,y,z } または dx/dy/dz が必要です（mm）。" };
            var offset = new XYZ(UnitHelper.MmToFt(deltaMm.X), UnitHelper.MmToFt(deltaMm.Y), UnitHelper.MmToFt(deltaMm.Z));

            using (var tx = new Transaction(doc, "Move Tag"))
            {
                tx.Start();
                try { ElementTransformUtils.MoveElement(doc, tag.Id, offset); }
                catch (Exception ex) { tx.RollBack(); return new { ok = false, msg = $"移動に失敗: {ex.Message}" }; }
                tx.Commit();
            }
            return new { ok = true, tagId = tag.Id.IntegerValue, uniqueId = tag.UniqueId, inputUnits = TagHelpers.UnitsIn(), internalUnits = TagHelpers.UnitsInt() };
        }
    }

    public class RotateTagCommand : IRevitCommandHandler
    {
        public string CommandName => "rotate_tag";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            Element tag = null;
            int tagId = p.Value<int?>("tagId") ?? 0;
            string tagUid = p.Value<string>("uniqueId");
            if (tagId > 0) tag = doc.GetElement(new ElementId(tagId));
            else if (!string.IsNullOrWhiteSpace(tagUid)) tag = doc.GetElement(tagUid);
            if (tag == null) return new { ok = false, msg = "Tag が見つかりません（tagId/uniqueId）。" };

            // 回転中心（既定: タグヘッド位置）
            XYZ center = null;
            if (InputPointReader.TryReadPointMm(p, out var centerMm, "center"))
            {
                center = new XYZ(UnitHelper.MmToFt(centerMm.X), UnitHelper.MmToFt(centerMm.Y), UnitHelper.MmToFt(centerMm.Z));
            }
            else
            {
                try { center = (tag as IndependentTag)?.TagHeadPosition ?? (tag.Location as LocationPoint)?.Point ?? XYZ.Zero; }
                catch { center = XYZ.Zero; }
            }

            // 角度（deg優先 / radもOK）
            if (!InputPointReader.TryReadAngleDeg(p, out var angleDeg))
                angleDeg = 0.0;
            double angleRad = UnitHelper.DegToInternal(angleDeg);
            var axis = Line.CreateBound(center, center + XYZ.BasisZ);

            using (var tx = new Transaction(doc, "Rotate Tag"))
            {
                tx.Start();
                try { ElementTransformUtils.RotateElement(doc, tag.Id, axis, angleRad); }
                catch (Exception ex) { tx.RollBack(); return new { ok = false, msg = $"回転に失敗: {ex.Message}" }; }
                tx.Commit();
            }
            return new { ok = true, tagId = tag.Id.IntegerValue, uniqueId = tag.UniqueId };
        }
    }

    public class GetTagParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_tag_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            Element tag = null;
            int tagId = p.Value<int?>("tagId") ?? 0;
            string tagUid = p.Value<string>("uniqueId");
            if (tagId > 0) tag = doc.GetElement(new ElementId(tagId));
            else if (!string.IsNullOrWhiteSpace(tagUid)) tag = doc.GetElement(tagUid);
            if (tag == null) return new { ok = false, msg = "Tag が見つかりません（tagId/uniqueId）。" };

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            var ordered = (tag.Parameters?.Cast<Parameter>() ?? Enumerable.Empty<Parameter>())
                .Select(pr => new { pr, name = pr?.Definition?.Name ?? "", id = pr?.Id.IntegerValue ?? -1 })
                .OrderBy(x => x.name).ThenBy(x => x.id).Select(x => x.pr).ToList();

            int totalCount = ordered.Count;

            if (count == 0)
                return new { ok = true, tagId = tag.Id.IntegerValue, uniqueId = tag.UniqueId, totalCount, inputUnits = TagHelpers.UnitsIn(), internalUnits = TagHelpers.UnitsInt() };

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(pr => pr?.Definition?.Name ?? "").ToList();
                return new { ok = true, tagId = tag.Id.IntegerValue, uniqueId = tag.UniqueId, totalCount, names, inputUnits = TagHelpers.UnitsIn(), internalUnits = TagHelpers.UnitsInt() };
            }

            var page = ordered.Skip(skip).Take(count);
            var result = new List<object>();
            foreach (var pr in page)
            {
                var mapped = UnitHelper.MapParameter(pr, doc, UnitsMode.SI, includeDisplay: true, includeRaw: true);
                result.Add(mapped);
            }

            return new { ok = true, tagId = tag.Id.IntegerValue, uniqueId = tag.UniqueId, totalCount, parameters = result, inputUnits = TagHelpers.UnitsIn(), internalUnits = TagHelpers.UnitsInt() };
        }
    }

    public class UpdateTagParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "update_tag_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            Element tag = null;
            int tagId = p.Value<int?>("tagId") ?? 0;
            string tagUid = p.Value<string>("uniqueId");
            if (tagId > 0) tag = doc.GetElement(new ElementId(tagId));
            else if (!string.IsNullOrWhiteSpace(tagUid)) tag = doc.GetElement(tagUid);
            if (tag == null) return new { ok = false, msg = "Tag が見つかりません（tagId/uniqueId）。" };

            string name = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(name) && p["builtInName"] == null && p["builtInId"] == null && p["guid"] == null)
                return new { ok = false, msg = "paramName または builtInName/builtInId/guid のいずれかが必要です。" };
            if (!p.TryGetValue("value", out var vtok)) return new { ok = false, msg = "value が必要です。" };

            var pr = ParamResolver.ResolveByPayload(tag, p, out var resolvedBy);
            if (pr == null) return new { ok = false, msg = $"Parameter not found (name/builtIn/guid)" };
            if (pr.IsReadOnly) return new { ok = false, msg = $"Parameter '{name}' は読み取り専用です。" };

            using (var tx = new Transaction(doc, "Update Tag Parameter"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                try
                {
                    string err;
                    var ok = UnitHelper.TrySetParameterByExternalValue(pr, (vtok as JValue)?.Value, out err);
                    if (!ok)
                    {
                        tx.RollBack();
                        return new { ok = false, msg = $"更新に失敗: {err}" };
                    }
                }
                catch (Exception ex) { tx.RollBack(); return new { ok = false, msg = $"更新に失敗: {ex.Message}" }; }
                tx.Commit();
            }
            return new { ok = true, tagId = tag.Id.IntegerValue, uniqueId = tag.UniqueId };
        }
    }

    public class DeleteTagCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_tag";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            Element tag = null;
            int tagId = p.Value<int?>("tagId") ?? 0;
            string tagUid = p.Value<string>("uniqueId");
            if (tagId > 0) tag = doc.GetElement(new ElementId(tagId));
            else if (!string.IsNullOrWhiteSpace(tagUid)) tag = doc.GetElement(tagUid);
            if (tag == null) return new { ok = false, msg = "Tag が見つかりません（tagId/uniqueId）。" };

            ICollection<ElementId> deleted = null;
            using (var tx = new Transaction(doc, "Delete Tag"))
            {
                tx.Start();
                try { deleted = doc.Delete(tag.Id); }
                catch (Exception ex) { tx.RollBack(); return new { ok = false, msg = $"削除に失敗: {ex.Message}" }; }
                tx.Commit();
            }

            var ids = deleted?.Select(x => x.IntegerValue).ToList() ?? new List<int>();
            return new { ok = true, tagId = tag.Id.IntegerValue, uniqueId = tag.UniqueId, deletedCount = ids.Count, deletedElementIds = ids };
        }
    }
}
