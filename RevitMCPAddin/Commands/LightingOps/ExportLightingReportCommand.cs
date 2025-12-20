// ================================================================
// Method : export_lighting_report
// ================================================================
#nullable enable
using System.Globalization;
using System.IO;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.LightingOps
{
    public sealed class ExportLightingReportCommand : IRevitCommandHandler
    {
        public string CommandName => "export_lighting_report";

        public object Execute(UIApplication uiapp, RequestCommand req)
        {
            try
            {
                var p = ReqShim.Params(req);

                string path = ReqShim.Get<string>(p, "path", "");
                if (string.IsNullOrWhiteSpace(path))
                    return new { ok = false, msg = "path required (*.csv)" };

                string scope = ReqShim.Get<string>(p, "scope", "rooms");
                var ids = ReqShim.Get<int[]>(p, "ids", new int[0]);
                double target = ReqShim.Get<double?>(p, "targetWPerM2", null) ?? 12.0;

                // 省エネチェック再利用
                var chkParams = new JObject
                {
                    ["scope"] = scope,
                    ["ids"] = JArray.FromObject(ids),
                    ["targetWPerM2"] = target
                };
                var chk = JObject.FromObject(new CheckLightingEnergyCommand()
                    .Execute(uiapp, new RequestCommandProxy(chkParams)));

                if (!(chk["ok"]?.Value<bool>() ?? false))
                    return new { ok = false, msg = "energy check failed" };

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var sw = new StreamWriter(path, false))
                {
                    sw.WriteLine("Scope,Id,Name,W_per_m2,Target,Status,OverRatio");
                    var arr = chk["results"] as JArray ?? new JArray();
                    foreach (var r in arr)
                    {
                        string line = string.Join(",",
                            scope,
                            r["id"]!.Value<int>().ToString(CultureInfo.InvariantCulture),
                            CsvEsc(r["name"]?.ToString() ?? ""),
                            r["wPerM2"]!.Value<double>().ToString(CultureInfo.InvariantCulture),
                            r["targetWPerM2"]!.Value<double>().ToString(CultureInfo.InvariantCulture),
                            r["status"]!.ToString(),
                            r["overRatio"]!.Value<double>().ToString(CultureInfo.InvariantCulture));
                        sw.WriteLine(line);
                    }
                }
                return new { ok = true, path };
            }
            catch (System.Exception ex)
            {
                RevitLogger.LogError("[export_lighting_report] " + ex);
                return new { ok = false, msg = ex.Message };
            }
        }

        private static string CsvEsc(string s)
        {
            return (s.Contains(",") || s.Contains("\"")) ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        private sealed class RequestCommandProxy : RequestCommand
        {
            public JObject ProxyParams { get; }
            public RequestCommandProxy(JObject p) { ProxyParams = p; }
            public JObject GetParams() => ProxyParams;
        }
    }
}
