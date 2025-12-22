// ================================================================
// File: Commands/ViewOps/SetCategoryVisibilityCommand.cs
// Purpose: JSON-RPC "set_category_visibility" for a view
// Params: { viewId:int, categoryIds:int[], visible:bool }
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;


namespace RevitMCPAddin.Commands.ViewOps
{
    public class SetCategoryVisibilityCommand : IRevitCommandHandler
    {
        public string CommandName => "set_category_visibility";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var uidoc = uiapp?.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc == null) return new { ok = false, msg = "No active document." };

                var p = (JObject?)(cmd.Params ?? new JObject());
                if (p == null) return new { ok = false, msg = "Missing params" };

                var viewId = Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("viewId"));
                var view = doc.GetElement(viewId) as View;
                if (view == null) return new { ok = false, msg = $"viewId={viewId.IntValue()} not found" };

                bool visible = p.Value<bool?>("visible") ?? true;
                var catIds = new List<int>();
                if (p.TryGetValue("categoryIds", out var catsTok) && catsTok is JArray arr)
                {
                    foreach (var t in arr) { try { catIds.Add(t.Value<int>()); } catch { } }
                }

                int changed = 0; var errors = new List<object>();
                using (var tx = new Transaction(doc, "[MCP] Set Category Visibility"))
                {
                    tx.Start();
                    foreach (var cid in catIds.Distinct())
                    {
                        try
                        {
                            var bic = (BuiltInCategory)cid;
                            var cat = Category.GetCategory(doc, bic);
                            if (cat == null) { errors.Add(new { categoryId = cid, error = "Category not found" }); continue; }
                            view.SetCategoryHidden(cat.Id, !visible);
                            changed++;
                        }
                        catch (Exception ex)
                        {
                            errors.Add(new { categoryId = cid, error = ex.Message });
                        }
                    }
                    tx.Commit();
                }

                return new { ok = true, changed, errors };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }
}



