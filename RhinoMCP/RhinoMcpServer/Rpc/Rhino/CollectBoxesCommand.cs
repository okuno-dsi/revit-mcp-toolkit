using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System;

namespace RhinoMcpServer.Rpc.Rhino
{
    public static class CollectBoxesCommand
    {
        // params: { "uniqueIds": [..] } (optional -> all)
        public static async Task<object> HandleAsync(JObject p)
        {
            var call = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["method"] = "rhino_collect_boxes",
                ["params"] = p
            };
            var resText = await PluginIpcClient.PostAsync(call.ToString());
            var jo = JObject.Parse(resText);
            return jo["result"] ?? new JObject { ["ok"] = false, ["msg"] = "no result" };
        }
    }
}

