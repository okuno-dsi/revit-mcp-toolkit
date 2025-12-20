// ================================================================
// File: Commands/VisualizationOps/ResetTagColorsCommand.cs
// Purpose: Reset per-element overrides applied to annotation tags
//          in the active view (or selection).
// Notes  : Based on Design/Revit_TagColorizer_Addin_DesignDoc.md
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.VisualizationOps
{
    public class ResetTagColorsCommand : IRevitCommandHandler
    {
        public string CommandName => "reset_tag_colors";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            var view = uidoc?.ActiveView;
            if (doc == null || view == null)
                return new { ok = false, msg = "アクティブビューまたはドキュメントが見つかりません。" };

            var p = (JObject?)(cmd.Params ?? new JObject()) ?? new JObject();

            var targetCatIds = BuildTargetCategoryIds(doc, p);
            if (targetCatIds.Count == 0)
            {
                return new
                {
                    ok = true,
                    viewId = view.Id.IntegerValue,
                    reset = 0,
                    message = "No valid tag categories to reset."
                };
            }

            var elements = GetTargetElements(doc, uidoc, view, targetCatIds);
            if (elements.Count == 0)
            {
                return new
                {
                    ok = true,
                    viewId = view.Id.IntegerValue,
                    reset = 0,
                    message = "No tag elements found in active view (or selection)."
                };
            }

            int resetCount = 0;
            var ogsEmpty = new OverrideGraphicSettings();

            using (var tx = new Transaction(doc, "[MCP] Reset Tag Color Overrides"))
            {
                tx.Start();

                foreach (var el in elements)
                {
                    view.SetElementOverrides(el.Id, ogsEmpty);
                    resetCount++;
                }

                tx.Commit();
            }

            try
            {
                doc.Regenerate();
                uidoc?.RefreshActiveView();
            }
            catch
            {
                // ignore
            }

            return new
            {
                ok = true,
                viewId = view.Id.IntegerValue,
                reset = resetCount,
                categories = targetCatIds.ToArray()
            };
        }

        private static HashSet<int> BuildTargetCategoryIds(Document doc, JObject p)
        {
            var ids = new HashSet<int>();

            var catNamesArr = p["targetCategories"] as JArray;
            List<string> names;
            if (catNamesArr != null)
            {
                names = catNamesArr
                    .Values<string>()
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
            }
            else
            {
                names = new List<string>
                {
                    "OST_RoomTags",
                    "OST_DoorTags",
                    "OST_WindowTags",
                    "OST_GenericAnnotation"
                };
            }

            foreach (var name in names)
            {
                try
                {
                    if (Enum.TryParse(name.Trim(), ignoreCase: true, out BuiltInCategory bic))
                    {
                        var cat = Category.GetCategory(doc, bic);
                        if (cat != null)
                            ids.Add(cat.Id.IntegerValue);
                    }
                }
                catch
                {
                    // ignore invalid names
                }
            }

            return ids;
        }

        private static List<Element> GetTargetElements(Document doc, UIDocument? uidoc, View view, HashSet<int> targetCatIds)
        {
            var result = new List<Element>();
            var selIds = uidoc?.Selection?.GetElementIds() ?? new List<ElementId>();

            // If there is an explicit selection, honor it as-is (do not filter by category).
            if (selIds != null && selIds.Count > 0)
            {
                foreach (var id in selIds)
                {
                    var el = doc.GetElement(id);
                    if (el != null)
                    {
                        result.Add(el);
                    }
                }
                return result;
            }

            // No selection: filter by target tag categories in the active view.
            var collector = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType();

            foreach (var el in collector)
            {
                var cat = el.Category;
                if (cat == null) continue;
                if (targetCatIds.Contains(cat.Id.IntegerValue))
                {
                    result.Add(el);
                }
            }

            return result;
        }
    }
}
