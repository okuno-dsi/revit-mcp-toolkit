// ================================================================
// Method : get_lighting_power_summary
// ================================================================
#nullable enable
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.LightingOps
{
    public sealed class GetLightingPowerSummaryCommand : IRevitCommandHandler
    {
        public string CommandName => "get_lighting_power_summary";

        public object Execute(UIApplication uiapp, RequestCommand req)
        {
            try
            {
                var doc = uiapp.ActiveUIDocument.Document;
                var p = ReqShim.Params(req);

                string scope = ReqShim.Get<string>(p, "scope", "document");
                int? viewId = ReqShim.Get<int?>(p, "viewId", null);
                int? levelId = ReqShim.Get<int?>(p, "levelId", null);
                var ids = ReqShim.Get<List<int>>(p, "ids", new List<int>());

                var fixtures = LightingCommon
                    .CollectFixtures(doc, scope == "view" ? viewId : null, scope == "level" ? levelId : null)
                    .ToList();

                double totalW = fixtures.Sum(LightingCommon.GetWatt);

                if (scope == "rooms" || scope == "spaces")
                {
                    var items = new JArray();
                    foreach (var id in ids)
                    {
                        var se = LightingCommon.GetSpatialElement(doc, Autodesk.Revit.DB.ElementIdCompat.From(id));
                        if (se == null)
                        {
                            items.Add(new JObject { ["id"] = id, ["ok"] = false, ["msg"] = "Not a Room/Space" });
                            continue;
                        }
                        double areaM2 = LightingCommon.GetAreaM2(se);
                        if (areaM2 <= 0)
                        {
                            items.Add(new JObject { ["id"] = id, ["ok"] = false, ["msg"] = "Area <= 0" });
                            continue;
                        }

                        double sumW = 0.0;
                        foreach (var f in fixtures)
                        {
                            var pt = LightingCommon.GetLocationPoint(f);
                            if (pt == null) continue;
                            if (LightingCommon.IsInside(se, pt)) sumW += LightingCommon.GetWatt(f);
                        }

                        items.Add(new JObject
                        {
                            ["id"] = id,
                            ["name"] = se.Name,
                            ["areaM2"] = areaM2,
                            ["totalW"] = sumW,
                            ["wPerM2"] = sumW / areaM2,
                            ["ok"] = true
                        });
                    }

                    return new { ok = true, scope, totalW, items };
                }

                // document / view / level
                return new { ok = true, scope, totalW };
            }
            catch (System.Exception ex)
            {
                RevitLogger.LogError("[get_lighting_power_summary] " + ex);
                return new { ok = false, msg = ex.Message };
            }
        }
    }
}

