using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System;

namespace RhinoMcpServer.Rpc.Rhino
{
    public static class Import3dmCommand
    {
        // params: { "path": "C:/path/file.3dm", "autoIndex": true, "units": "mm" }
        public static async Task<object> HandleAsync(JObject p)
        {
            var path = (string)(p["path"] ?? "");
            if (string.IsNullOrWhiteSpace(path))
                return new JObject { ["ok"] = false, ["msg"] = "path required" };

            var call = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["method"] = "rhino_import_3dm",
                ["params"] = new JObject
                {
                    ["path"] = path,
                    ["autoIndex"] = p["autoIndex"] ?? true,
                    ["units"] = p["units"] ?? "mm"
                }
            };
            var resText = await PluginIpcClient.PostAsync(call.ToString());
            var jo = JObject.Parse(resText);
            return jo["result"] ?? new JObject { ["ok"] = false, ["msg"] = "no result" };
        }
    }
}

