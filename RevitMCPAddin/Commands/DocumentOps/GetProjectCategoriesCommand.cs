// File: Commands/DocumentOps/GetProjectCategoriesCommand.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCPAddin.Commands.DocumentOps
{
    /// <summary>
    /// アクティブなプロジェクトに含まれるカテゴリ一覧を返す
    /// </summary>
    public class GetProjectCategoriesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_project_categories";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var categories = doc.Settings.Categories;

            var list = new List<object>();
            foreach (Category cat in categories)
            {
                if (cat == null) continue;

                list.Add(new
                {
                    categoryId = cat.Id.IntegerValue,
                    name = cat.Name,
                    parentId = cat.Parent != null ? cat.Parent.Id.IntegerValue : (int?)null,
                    categoryType = cat.CategoryType.ToString(),
                    isTagCategory = cat.IsTagCategory,
                    allowsBoundParameters = cat.AllowsBoundParameters
                });
            }

            return new
            {
                ok = true,
                totalCount = list.Count,
                categories = list
            };
        }
    }
}
