// ================================================================
// File: Commands/ViewOps/GetCategoriesUsedInViewCommand.cs
// Revit 2023 / .NET Framework 4.8
// - Revit 2024+ では色も取得（GetProjectionLineColorが存在する場合）
// ================================================================
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;              // ★ 追加：反射で Getter の有無を判定
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ViewOps
{
    public class GetCategoriesUsedInViewCommand : IRevitCommandHandler
    {
        public string CommandName => "get_categories_used_in_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null) return new { ok = false, msg = "No active document." };

                var p = cmd.Params as JObject ?? new JObject();
                var viewId = new ElementId((int)p.Value<int?>("viewId"));
                var view = doc.GetElement(viewId) as View;
                if (view == null) return new { ok = false, msg = $"View not found: {viewId.IntegerValue}" };

                // ビューに「表示対象として存在する」要素だけ
                var elems = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .ToElements();

                // ★ 反射で GetProjectionLineColor が存在するか確認
                var ogsType = typeof(OverrideGraphicSettings);
                var getProjLineColor = ogsType.GetMethod("GetProjectionLineColor", BindingFlags.Instance | BindingFlags.Public);

                var catGroups = elems
                    .Select(e => e.Category)
                    .Where(c => c != null)
                    .GroupBy(c => c.Id.IntegerValue);

                var items = new List<object>();
                foreach (var g in catGroups)
                {
                    var cat = g.First();
                    var catId = cat.Id;
                    var visible = !view.GetCategoryHidden(catId);

                    object colorObj = null;
                    try
                    {
                        // 2024+ なら取得、それ以外は null のまま
                        if (getProjLineColor != null)
                        {
                            var ogs = view.GetCategoryOverrides(catId);
                            var color = getProjLineColor.Invoke(ogs, null) as Color;
                            if (color != null)
                            {
                                colorObj = new { r = (int)color.Red, g = (int)color.Green, b = (int)color.Blue };
                            }
                        }
                    }
                    catch
                    {
                        // 取得に失敗した場合は無視（nullのまま）
                    }

                    items.Add(new
                    {
                        categoryId = catId.IntegerValue,
                        name = cat.Name,
                        elementCount = g.Count(),
                        visible,
                        projectionLineColor = colorObj  // 2023では常にnull、2024+では取得可
                    });
                }

                items = items.OrderBy(x => ((dynamic)x).name as string).ToList();

                return new
                {
                    ok = true,
                    viewId = view.Id.IntegerValue,
                    categories = items
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }
}
