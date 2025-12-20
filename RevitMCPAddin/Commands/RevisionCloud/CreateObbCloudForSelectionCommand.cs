#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.RevisionCloud
{
    /// <summary>
    /// create_obb_cloud_for_selection: Draw OBB-aligned revision clouds for current selection (or stashed selection).
    /// Params: { widthMm?:number, paddingMm?:number, ensureCloudVisible?:bool }
    /// Returns: passthrough from CreateRevisionCloudForElementProjectionCommand (count/cloudIds...)
    /// </summary>
    public class CreateObbCloudForSelectionCommand : IRevitCommandHandler
    {
        public string CommandName => "create_obb_cloud_for_selection";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, code = "NO_DOC", msg = "No active document." };

            var p = (JObject)(cmd.Params ?? new JObject());
            double? widthMm = p.Value<double?>("widthMm");
            double? paddingMm = p.Value<double?>("paddingMm");
            bool ensureVisible = p.Value<bool?>("ensureCloudVisible") ?? true;

            // Resolve selection (active or stashed)
            var selIds = uidoc.Selection.GetElementIds().Select(x => x.IntegerValue).ToList();
            if (selIds.Count == 0)
            {
                var stashed = SelectionStash.GetIds();
                if (stashed != null && stashed.Length > 0) selIds = stashed.ToList();
            }
            if (selIds.Count == 0)
                return new { ok = false, code = "NO_SELECTION", msg = "No selected elements found." };

            // Ensure revision cloud category visible in active view
            if (ensureVisible)
            {
                try
                {
                    var v = uidoc.ActiveGraphicalView ?? uidoc.ActiveView as View;
                    if (v != null && v.ViewType != ViewType.ProjectBrowser)
                    {
                        using (var tx = new Transaction(doc, "Ensure RevisionCloud Visible"))
                        {
                            tx.Start();
                            var cat = Category.GetCategory(doc, BuiltInCategory.OST_RevisionClouds);
                            if (cat != null) v.SetCategoryHidden(cat.Id, false);
                            tx.Commit();
                        }
                    }
                }
                catch { /* best-effort */ }
            }

            // Delegate to existing projection command
            var inner = new JObject
            {
                ["elementIds"] = new JArray(selIds),
                ["mode"] = "obb",
                ["preZoom"] = "",
                ["restoreZoom"] = false,
                ["focusMarginMm"] = 150.0
            };
            if (widthMm.HasValue) inner["widthMm"] = widthMm.Value;
            if (paddingMm.HasValue) inner["paddingMm"] = paddingMm.Value;

            var handler = new CreateRevisionCloudForElementProjectionCommand();
            var res = handler.Execute(uiapp, new RequestCommand { Method = "create_revision_cloud_for_element_projection", Params = inner });
            return res;
        }
    }
}
