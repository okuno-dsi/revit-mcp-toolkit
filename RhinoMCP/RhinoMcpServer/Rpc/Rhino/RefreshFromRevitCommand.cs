using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System;

namespace RhinoMcpServer.Rpc.Rhino
{
    public static class RefreshFromRevitCommand
    {
        public static Task<object> HandleAsync(JObject p)
        {
            // For now, just notify plugin; full Revit fetch can be added later
            var call = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["method"] = "rhino_refresh_from_revit",
                ["params"] = p
            };
            return Forward(call);
        }

        private static async Task<object> Forward(JObject call)
        {
            var resText = await PluginIpcClient.PostAsync(call.ToString());
            var jo = JObject.Parse(resText);
            return jo["result"] ?? new JObject { ["ok"] = false, ["msg"] = "no result" };
        }
    }
}
