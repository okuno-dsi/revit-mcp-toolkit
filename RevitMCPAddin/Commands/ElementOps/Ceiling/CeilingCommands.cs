// File: RevitMCPAddin/Commands/ElementOps/Ceiling/CeilingTypeCommands.cs
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Ceiling
{
    internal static class CeilingTypeUtil
    {
        /// <summary>typeId / typeName(+familyName) の順で CeilingType を解決。null なら未解決。</summary>
        public static CeilingType ResolveTypeByArgs(Document doc, JObject p)
        {
            int typeId = p.Value<int?>("typeId") ?? p.Value<int?>("ceilingTypeId") ?? 0;
            string typeName = p.Value<string>("typeName");
            string familyName = p.Value<string>("familyName");

            if (typeId > 0)
            {
                var t = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(typeId)) as CeilingType;
                return t;
            }

            if (!string.IsNullOrWhiteSpace(typeName))
            {
                var q = new FilteredElementCollector(doc)
                    .OfClass(typeof(CeilingType))
                    .WhereElementIsElementType()
                    .Cast<CeilingType>()
                    .Where(ct => string.Equals(ct.Name ?? "", typeName, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(familyName))
                    q = q.Where(ct => string.Equals(ct.FamilyName ?? "", familyName, StringComparison.OrdinalIgnoreCase));

                return q.OrderBy(ct => ct.FamilyName ?? "")
                        .ThenBy(ct => ct.Name ?? "")
                        .FirstOrDefault();
            }

            return null;
        }

        /// <summary>typeName の天井タイプを検索（厳密一致）。</summary>
        public static CeilingType FindByName(Document doc, string typeName, string familyName = null)
        {
            var q = new FilteredElementCollector(doc)
                .OfClass(typeof(CeilingType))
                .WhereElementIsElementType()
                .Cast<CeilingType>()
                .Where(ct => string.Equals(ct.Name ?? "", typeName ?? "", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(familyName))
                q = q.Where(ct => string.Equals(ct.FamilyName ?? "", familyName, StringComparison.OrdinalIgnoreCase));
            return q.FirstOrDefault();
        }

        /// <summary>指定 typeId を参照している Ceiling インスタンス件数を返す。</summary>
        public static int CountInstancesOfType(Document doc, ElementId typeId)
        {
            if (typeId == null || typeId == ElementId.InvalidElementId) return 0;
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Autodesk.Revit.DB.Ceiling))
                .WhereElementIsNotElementType()
                .Count(e => e.GetTypeId() == typeId);
        }
    }

    /// <summary>Duplicate an existing CeilingType under a new name (robust).</summary>
    public class DuplicateCeilingTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "duplicate_ceiling_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params;

            // 元タイプ解決: typeId / typeName(+familyName)
            var src = CeilingTypeUtil.ResolveTypeByArgs(doc, p);
            if (src == null) return new { ok = false, msg = "CeilingType が解決できません（typeId または typeName(+familyName) を指定）。" };

            // newName と重名ポリシー
            string newName = p.Value<string>("newName");
            if (string.IsNullOrWhiteSpace(newName)) return new { ok = false, msg = "newName が必要です。" };

            string ifExists = (p.Value<string>("ifExists") ?? "error").ToLowerInvariant(); // error|reuse|suffix

            var existing = CeilingTypeUtil.FindByName(doc, newName, src.FamilyName);
            if (existing != null)
            {
                if (ifExists == "reuse")
                {
                    return new
                    {
                        ok = true,
                        existed = true,
                        newTypeId = existing.Id.IntValue(),
                        newTypeName = existing.Name,
                        uniqueId = existing.UniqueId
                    };
                }
                if (ifExists == "suffix")
                {
                    // ユニーク名を自動生成
                    int i = 1;
                    string baseName = newName;
                    while (CeilingTypeUtil.FindByName(doc, newName, src.FamilyName) != null)
                    {
                        i++;
                        newName = $"{baseName} ({i})";
                    }
                    // そのまま複製へ
                }
                else
                {
                    return new { ok = false, msg = $"同名の CeilingType が存在します: \"{newName}\"" };
                }
            }

            CeilingType newCt = null;
            using (var tx = new Transaction(doc, "Duplicate CeilingType"))
            {
                tx.Start();
                try
                {
                    newCt = src.Duplicate(newName) as CeilingType;
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = $"複製に失敗: {ex.Message}" };
                }
                tx.Commit();
            }

            return new
            {
                ok = true,
                newTypeId = newCt.Id.IntValue(),
                newTypeName = newCt.Name,
                uniqueId = newCt.UniqueId,
                familyName = newCt.FamilyName ?? ""
            };
        }
    }

    /// <summary>Delete a CeilingType by its id or name. Checks usage; supports 'force'.</summary>
    public class DeleteCeilingTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_ceiling_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params;
            bool force = p.Value<bool?>("force") ?? false;

            // タイプ解決
            var ct = CeilingTypeUtil.ResolveTypeByArgs(doc, p);
            if (ct == null) return new { ok = false, msg = "CeilingType が解決できません（typeId または typeName(+familyName)）。" };

            // 使用中チェック
            int inUse = 0;
            try { inUse = CeilingTypeUtil.CountInstancesOfType(doc, ct.Id); } catch { inUse = 0; }

            if (inUse > 0 && !force)
                return new { ok = false, msg = $"タイプは {inUse} 件のインスタンスで使用中です。force=true で削除を試みます。" };

            ICollection<ElementId> deleted = null;
            using (var tx = new Transaction(doc, "Delete CeilingType"))
            {
                tx.Start();
                try
                {
                    deleted = doc.Delete(ct.Id);
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = $"削除に失敗: {ex.Message}" };
                }
                tx.Commit();
            }

            var ids = deleted?.Select(x => x.IntValue()).ToList() ?? new List<int>();
            return new
            {
                ok = true,
                typeId = ct.Id.IntValue(),
                uniqueId = ct.UniqueId,
                deletedCount = ids.Count,
                deletedElementIds = ids
            };
        }
    }

    /// <summary>List ceiling types with filters, paging, namesOnly.</summary>
    public class GetCeilingTypesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_ceiling_types";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params;
            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;
            string filterTypeName = p.Value<string>("typeName");
            string filterFamilyName = p.Value<string>("familyName");
            string nameContains = p.Value<string>("nameContains");

            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(CeilingType))
                .WhereElementIsElementType()
                .Cast<CeilingType>()
                .ToList();

            IEnumerable<CeilingType> q = all;

            if (!string.IsNullOrWhiteSpace(filterTypeName))
                q = q.Where(ct => string.Equals(ct.Name ?? "", filterTypeName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(filterFamilyName))
                q = q.Where(ct => string.Equals(ct.FamilyName ?? "", filterFamilyName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(nameContains))
                q = q.Where(ct => (ct.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);

            var ordered = q
                .Select(ct => new { ct, fam = ct.FamilyName ?? "", name = ct.Name ?? "", id = ct.Id.IntValue() })
                .OrderBy(x => x.fam).ThenBy(x => x.name).ThenBy(x => x.id)
                .Select(x => x.ct)
                .ToList();

            int totalCount = ordered.Count;

            if (count == 0)
                return new { ok = true, totalCount };

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(ct => ct.Name ?? "").ToList();
                return new { ok = true, totalCount, names };
            }

            var list = ordered.Skip(skip).Take(count)
                .Select(ct => new
                {
                    typeId = ct.Id.IntValue(),
                    uniqueId = ct.UniqueId,
                    typeName = ct.Name ?? "",
                    familyName = ct.FamilyName ?? ""
                })
                .ToList();

            return new { ok = true, totalCount, ceilingTypes = list };
        }
    }
}


