// ================================================================
// File   : Commands/AnnotationOps/DoorWindowLegendTemplateCommands.cs
// Target : .NET Framework 4.8 / Revit 2023+
// Purpose: Template-based Door/Window Legend population
//          - populate_door_window_legend_from_template
// Notes  :
//  - LegendComponent API が公開されていない前提で、
//    既存 Legend コンポーネントをコピーしてタイプ変更を試みる。
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.AnnotationOps
{
    public class PopulateDoorWindowLegendFromTemplateCommand : IRevitCommandHandler
    {
        public string CommandName => "populate_door_window_legend_from_template";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp != null ? uiapp.ActiveUIDocument : null;
            var doc = uidoc != null ? uidoc.Document : null;
            if (doc == null)
            {
                return new { ok = false, msg = "アクティブドキュメントがありません。" };
            }

            // パフォーマンス/タイムアウト問題のため、このコマンドは廃止。
            // 代わりに以下のコマンド群を利用してください:
            //  - copy_legend_components_between_views
            //  - layout_legend_components_in_view
            //  - （必要なら）個別のタイプ変更コマンド
            return new
            {
                ok = false,
                msg = "populate_door_window_legend_from_template は現在使用できません。copy_legend_components_between_views と layout_legend_components_in_view などの軽量コマンドを組み合わせてご利用ください。"
            };

        }

        private static View FindLegendViewByName(Document doc, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(v =>
                    v.ViewType == ViewType.Legend &&
                    string.Equals(v.Name ?? string.Empty, name, StringComparison.OrdinalIgnoreCase));
        }

        private class LegendTypeInfo
        {
            public string Category;
            public string Mark;
            public ElementId TypeId;
        }

        private class LegendPlacementItem
        {
            public string Category;
            public string Mark;
            public ElementId SymbolId;
        }

        private static List<LegendTypeInfo> CollectDoorWindowTypes(
            Document doc,
            bool includeDoors,
            bool includeWindows,
            string markParamName,
            bool onlyUsed)
        {
            var result = new List<LegendTypeInfo>();
            var instanceCountBySymbol = new Dictionary<ElementId, int>();

            if (includeDoors)
            {
                var insts = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Doors)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>();
                foreach (var fi in insts)
                {
                    var sid = fi.Symbol != null ? fi.Symbol.Id : null;
                    if (sid == null) continue;
                    int c;
                    instanceCountBySymbol.TryGetValue(sid, out c);
                    instanceCountBySymbol[sid] = c + 1;
                }
            }

            if (includeWindows)
            {
                var insts = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Windows)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>();
                foreach (var fi in insts)
                {
                    var sid = fi.Symbol != null ? fi.Symbol.Id : null;
                    if (sid == null) continue;
                    int c;
                    instanceCountBySymbol.TryGetValue(sid, out c);
                    instanceCountBySymbol[sid] = c + 1;
                }
            }

            if (includeDoors)
            {
                CollectFromCategory(doc, BuiltInCategory.OST_Doors, "Doors",
                    markParamName, onlyUsed, instanceCountBySymbol, result);
            }

            if (includeWindows)
            {
                CollectFromCategory(doc, BuiltInCategory.OST_Windows, "Windows",
                    markParamName, onlyUsed, instanceCountBySymbol, result);
            }

            return result;
        }

        private static void CollectFromCategory(
            Document doc,
            BuiltInCategory bic,
            string categoryLabel,
            string markParamName,
            bool onlyUsed,
            Dictionary<ElementId, int> instanceCountBySymbol,
            List<LegendTypeInfo> result)
        {
            var symbols = new FilteredElementCollector(doc)
                .OfCategory(bic)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>();

            foreach (var sym in symbols)
            {
                int count = 0;
                instanceCountBySymbol.TryGetValue(sym.Id, out count);
                if (onlyUsed && count == 0) continue;

                string mark = GetDoorWindowTypesForScheduleCommand.ResolveMark(sym, markParamName);
                if (string.IsNullOrWhiteSpace(mark)) continue;

                result.Add(new LegendTypeInfo
                {
                    Category = categoryLabel,
                    Mark = mark,
                    TypeId = sym.Id
                });
            }
        }

        private static XYZ GetElementCenter(Element e, View view)
        {
            try
            {
                var loc = e.Location as LocationPoint;
                if (loc != null) return loc.Point;
            }
            catch
            {
                // ignore
            }

            try
            {
                var bbox = e.get_BoundingBox(view);
                if (bbox != null)
                {
                    return (bbox.Min + bbox.Max) * 0.5;
                }
            }
            catch
            {
                // ignore
            }

            return new XYZ(0, 0, 0);
        }
    }
}
