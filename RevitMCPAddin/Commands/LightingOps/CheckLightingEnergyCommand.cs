// ================================================================
// Method : check_lighting_energy
// ================================================================
#nullable enable
using System.Collections.Generic;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.LightingOps
{
    public sealed class CheckLightingEnergyCommand : IRevitCommandHandler
    {
        public string CommandName => "check_lighting_energy";

        public object Execute(UIApplication uiapp, RequestCommand req)
        {
            try
            {
                var p = ReqShim.Params(req);
                string scope = ReqShim.Get<string>(p, "scope", "rooms");
                var ids = ReqShim.Get<List<int>>(p, "ids", new List<int>());
                double target = ReqShim.Get<double?>(p, "targetWPerM2", null) ?? 12.0;

                if (scope != "rooms" && scope != "spaces")
                    return new { ok = false, msg = "scope must be 'rooms' or 'spaces'" };

                // 内部呼び出し
                var summaryReq = new RequestCommand();
                var sp = new JObject { ["scope"] = scope, ["ids"] = JArray.FromObject(ids) };
                // RequestShim 経由で扱うので command プロパティは不要。直接クラスを呼ぶ
                var summaryObj = JObject.FromObject(new GetLightingPowerSummaryCommand()
                    .Execute(uiapp, new RequestCommandProxy(sp)));

                if (!(summaryObj["ok"]?.Value<bool>() ?? false))
                    return new { ok = false, msg = "power summary failed" };

                var results = new JArray();
                var items = summaryObj["items"] as JArray ?? new JArray();
                foreach (var item in items)
                {
                    if (!(item["ok"]?.Value<bool>() ?? false))
                    {
                        results.Add(new JObject { ["id"] = item["id"], ["ok"] = false, ["msg"] = item["msg"] });
                        continue;
                    }
                    double wpm2 = item["wPerM2"]!.Value<double>();
                    var status = wpm2 <= target ? "OK" : "NG";
                    double over = wpm2 <= target ? 0.0 : (wpm2 - target) / System.Math.Max(target, 1e-9);

                    results.Add(new JObject
                    {
                        ["id"] = item["id"],
                        ["name"] = item["name"]?.ToString() ?? "",
                        ["wPerM2"] = wpm2,
                        ["targetWPerM2"] = target,
                        ["status"] = status,
                        ["overRatio"] = over
                    });
                }

                return new { ok = true, scope, results };
            }
            catch (System.Exception ex)
            {
                RevitLogger.LogError("[check_lighting_energy] " + ex);
                return new { ok = false, msg = ex.Message };
            }
        }

        // ---- 内部呼び出し用の簡易プロキシ（ReqShimが読める形で Params を渡す） ----
        private sealed class RequestCommandProxy : RequestCommand
        {
            public JObject ProxyParams { get; }
            public RequestCommandProxy(JObject p) { ProxyParams = p; }
            public JObject GetParams() => ProxyParams;
        }
    }
}
