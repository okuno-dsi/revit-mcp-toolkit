using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Rhino.FileIO;

namespace RhinoMcpServer.Rpc.Tools
{
    public static class Inspect3dmCommand
    {
        // params: { "path": "C:/.../file.3dm" }
        public static Task<object> HandleAsync(JObject p)
        {
            string path = (string)(p["path"] ?? "");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return Task.FromResult<object>(new JObject { ["ok"] = false, ["msg"] = "file not found" });
            try
            {
                var f = File3dm.Read(path);
                if (f == null) return Task.FromResult<object>(new JObject { ["ok"] = false, ["msg"] = "read failed" });
                var units = f.Settings.ModelUnitSystem.ToString();
                return Task.FromResult<object>(new JObject
                {
                    ["ok"] = true,
                    ["units"] = units,
                    ["objectCount"] = f.Objects.Count
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult<object>(new JObject { ["ok"] = false, ["msg"] = ex.Message });
            }
        }
    }
}
