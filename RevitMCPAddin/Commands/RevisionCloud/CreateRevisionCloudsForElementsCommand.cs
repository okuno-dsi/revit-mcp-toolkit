#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.RevisionCloud
{
    /// <summary>
    /// create_revision_clouds_for_elements: Thin wrapper over CreateRevisionCloudForElementProjectionCommand for batch creation.
    /// Params:
    ///   - viewId: int
    ///   - elementIds: int[]
    ///   - paddingMm?: number (default 150)
    ///   - commentTemplate?: string (optional; ignored in current implementation)
    /// Returns: { ok:true, created:int, cloudIds:int[] }
    /// </summary>
    public class CreateRevisionCloudsForElementsCommand : IRevitCommandHandler
    {
        public string CommandName => "create_revision_clouds_for_elements";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)(cmd.Params ?? new JObject());
            // Optional execution guard
            var guard = RevitMCPAddin.Core.ExpectedContextGuard.Validate(uiapp, p);
            if (guard != null) return guard;

            // Ensure required fields
            int viewId = p.Value<int?>("viewId") ?? 0;
            var ids = p["elementIds"] as JArray;
            if (viewId <= 0 || ids == null || ids.Count == 0)
                return new { ok = false, code = "BAD_PARAMS", msg = "viewId and elementIds are required." };

            // Build payload for existing robust command (AABB mode)
            var inner = new JObject
            {
                ["viewId"] = viewId,
                ["elementIds"] = new JArray(ids),
                ["mode"] = "aabb",
                ["paddingMm"] = p.Value<double?>("paddingMm") ?? 150.0,
                ["preZoom"] = p.Value<string>("preZoom") ?? string.Empty,
                ["restoreZoom"] = p.Value<bool?>("restoreZoom") ?? false,
                ["focusMarginMm"] = p.Value<double?>("focusMarginMm") ?? 150.0
            };

            var handler = new CreateRevisionCloudForElementProjectionCommand();
            var rc = handler.Execute(uiapp, new RequestCommand
            {
                Method = "create_revision_cloud_for_element_projection",
                Params = inner
            });

            return rc; // pass-through
        }
    }
}
