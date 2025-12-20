#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps
{
    /// <summary>
    /// get_elements_by_category_and_level
    /// 単一の Revit カテゴリ + レベル指定で要素IDを取得するコマンド。
    /// Design: Codex/Design/RevitMCP_GetElementsByCategoryAndLevel_DesignDoc.md（単一カテゴリ版）
    /// </summary>
    public class GetElementsByCategoryAndLevelCommand : IRevitCommandHandler
    {
        public string CommandName => "get_elements_by_category_and_level";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject?)(cmd.Params ?? new JObject()) ?? new JObject();

            // --- category ---
            var rawCategory = (p.Value<string>("category") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(rawCategory))
            {
                return new
                {
                    ok = false,
                    msg = "category が必要です。例: \"Walls\", \"Floors\", \"Structural Framing\""
                };
            }

            var (catOk, bic, catDebugName, catErr) = TryResolveCategory(rawCategory);
            if (!catOk)
            {
                return new
                {
                    ok = false,
                    msg = catErr ?? $"Unsupported category '{rawCategory}'.",
                    items = Array.Empty<object>()
                };
            }

            // --- levelSelector ---
            var levelSel = p["levelSelector"] as JObject ?? new JObject();
            string? levelName = levelSel.Value<string>("levelName")?.Trim();
            int? levelIdOpt = levelSel.Value<int?>("levelId");

            if ((!levelIdOpt.HasValue || levelIdOpt.Value <= 0) && string.IsNullOrWhiteSpace(levelName))
            {
                return new
                {
                    ok = false,
                    msg = "levelSelector.levelName または levelSelector.levelId のいずれかが必要です。",
                    items = Array.Empty<object>()
                };
            }

            Level? level = null;
            string levelResolvedBy = "";

            if (levelIdOpt.HasValue && levelIdOpt.Value > 0)
            {
                level = doc.GetElement(new ElementId(levelIdOpt.Value)) as Level;
                if (level == null)
                {
                    return new
                    {
                        ok = false,
                        msg = $"Level not found. levelId={levelIdOpt.Value}",
                        items = Array.Empty<object>()
                    };
                }
                levelResolvedBy = "LevelId";
            }
            else if (!string.IsNullOrWhiteSpace(levelName))
            {
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .ToList();

                level = levels.FirstOrDefault(l => string.Equals(l.Name, levelName, StringComparison.Ordinal));
                levelResolvedBy = "NameExact";
                if (level == null)
                {
                    level = levels.FirstOrDefault(l => string.Equals(l.Name, levelName, StringComparison.OrdinalIgnoreCase));
                    levelResolvedBy = "NameCaseInsensitive";
                }

                if (level == null)
                {
                    return new
                    {
                        ok = false,
                        msg = $"Level not found. levelName='{levelName}' levelId=null",
                        items = Array.Empty<object>()
                    };
                }
            }

            if (level == null)
            {
                return new
                {
                    ok = false,
                    msg = "Level 解決に失敗しました。",
                    items = Array.Empty<object>()
                };
            }

            // --- options ---
            var opt = p["options"] as JObject;
            bool includeTypeInfo = opt?.Value<bool?>("includeTypeInfo") ?? false;
            bool includeCategoryName = opt?.Value<bool?>("includeCategoryName") ?? true;
            int maxResults = opt?.Value<int?>("maxResults") ?? 0;

            // --- collect candidates by category ---
            var collector = new FilteredElementCollector(doc)
                .OfCategory(bic)
                .WhereElementIsNotElementType();

            var candidates = collector.ToElements().ToList();
            int totalCandidates = candidates.Count;

            var items = new List<object>();
            var lvlId = level.Id;
            string lvlName = level.Name;

            foreach (var e in candidates)
            {
                var elemLevelId = ResolveElementLevelId(e);
                if (elemLevelId == null || elemLevelId == ElementId.InvalidElementId)
                    continue;

                if (elemLevelId.IntegerValue != lvlId.IntegerValue)
                    continue;

                int eid = e.Id.IntegerValue;
                int levelIdInt = elemLevelId.IntegerValue;
                string? categoryLabel = includeCategoryName ? e.Category?.Name ?? string.Empty : null;
                int? typeIdInt = includeTypeInfo ? e.GetTypeId().IntegerValue : (int?)null;

                items.Add(new
                {
                    elementId = eid,
                    uniqueId = e.UniqueId,
                    categoryName = categoryLabel,
                    levelId = levelIdInt,
                    levelName = lvlName,
                    typeId = typeIdInt
                });

                if (maxResults > 0 && items.Count >= maxResults)
                    break;
            }

            string msg = items.Count > 0
                ? $"Found {items.Count} elements in category '{rawCategory}' on level '{lvlName}'."
                : $"No elements found for category '{rawCategory}' on level '{lvlName}'.";

            return new
            {
                ok = true,
                msg,
                items,
                debug = new
                {
                    category = catDebugName,
                    levelResolvedBy,
                    totalCandidates,
                    filteredCount = items.Count
                }
            };
        }

        private static ElementId? ResolveElementLevelId(Element e)
        {
            try
            {
                switch (e)
                {
                    case Autodesk.Revit.DB.Wall w:
                        return w.LevelId;
                    case Autodesk.Revit.DB.Floor f:
                        return f.LevelId;
                    case Autodesk.Revit.DB.Ceiling c:
                        return c.LevelId;
                    case Autodesk.Revit.DB.RoofBase rb:
                        return rb.LevelId;
                    case Autodesk.Revit.DB.FamilyInstance fi:
                        // FamilyInstance は LevelId が無効なケースがある（構造フレームなど）。
                        // 有効なときだけ優先し、無効なときはパラメータ経由の解決にフォールバックする。
                        if (fi.LevelId != ElementId.InvalidElementId)
                            return fi.LevelId;
                        break;
                    case Autodesk.Revit.DB.Architecture.Room room:
                        return room.LevelId;
                    case Autodesk.Revit.DB.Mechanical.Space space:
                        return space.LevelId;
                }

                // 一般要素向け: LEVEL_PARAM / INSTANCE_REFERENCE_LEVEL_PARAM / FAMILY_LEVEL_PARAM / INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM を順に試す
                var pLevel = e.get_Parameter(BuiltInParameter.LEVEL_PARAM)
                             ?? e.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)
                             ?? e.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                             ?? e.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
                if (pLevel != null && pLevel.StorageType == StorageType.ElementId)
                    return pLevel.AsElementId();
            }
            catch
            {
                // 失敗時は null（フィルタから除外）
            }

            return null;
        }

        private static (bool ok, BuiltInCategory category, string categoryName, string? msg) TryResolveCategory(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return (false, BuiltInCategory.INVALID, input, "category is required.");

            string norm = input.Trim();

            // よく使うカテゴリをフレンドリ名 + enum 名でマップ
            var map = new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase)
            {
                { "Walls", BuiltInCategory.OST_Walls },
                { "Wall", BuiltInCategory.OST_Walls },
                { "Floors", BuiltInCategory.OST_Floors },
                { "Floor", BuiltInCategory.OST_Floors },
                { "Structural Framing", BuiltInCategory.OST_StructuralFraming },
                { "Structural Frame", BuiltInCategory.OST_StructuralFraming },
                { "Structural Columns", BuiltInCategory.OST_StructuralColumns },
                { "Structural Column", BuiltInCategory.OST_StructuralColumns },
                { "Rooms", BuiltInCategory.OST_Rooms },
                { "Room", BuiltInCategory.OST_Rooms },
                { "Spaces", BuiltInCategory.OST_MEPSpaces },
                { "Space", BuiltInCategory.OST_MEPSpaces },
                { "Areas", BuiltInCategory.OST_Areas },
                { "Area", BuiltInCategory.OST_Areas },
                { "Doors", BuiltInCategory.OST_Doors },
                { "Door", BuiltInCategory.OST_Doors },
                { "Windows", BuiltInCategory.OST_Windows },
                { "Window", BuiltInCategory.OST_Windows },
            };

            if (map.TryGetValue(norm, out var bic))
                return (true, bic, norm, null);

            // BuiltInCategory の列挙名を直接指定された場合（例: "OST_Walls"）
            if (Enum.TryParse<BuiltInCategory>(norm, true, out var bic2)
                && bic2 != BuiltInCategory.INVALID)
            {
                return (true, bic2, bic2.ToString(), null);
            }

            return (false, BuiltInCategory.INVALID, norm, $"Unsupported category '{input}'.");
        }
    }
}
