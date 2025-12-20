using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Rhino.FileIO;

namespace RhinoMcpServer.Rpc.Tools
{
    public static class Convert3dmVersionCommand
    {
        // params: { "src": "C:/path/file.3dm", "dst": "optional path", "version": 7 }
        public static Task<object> HandleAsync(JObject p)
        {
            string src = (string)(p["src"] ?? "");
            if (string.IsNullOrWhiteSpace(src) || !File.Exists(src))
                return Task.FromResult<object>(new JObject { ["ok"] = false, ["msg"] = "src not found" });

            string dst = (string)(p["dst"] ?? "");
            int version = (int?)p["version"] ?? 7;
            if (version < 2 || version > 8) version = 7;

            try
            {
                var file = File3dm.Read(src);
                if (file == null)
                    return Task.FromResult<object>(new JObject { ["ok"] = false, ["msg"] = "failed to read 3dm" });

                string outPath = string.IsNullOrWhiteSpace(dst) ? src : dst;
                var folder = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                if (File.Exists(outPath)) File.Delete(outPath);

                file.Write(outPath, version);

                return Task.FromResult<object>(new JObject
                {
                    ["ok"] = true,
                    ["src"] = src,
                    ["dst"] = outPath,
                    ["version"] = version
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult<object>(new JObject { ["ok"] = false, ["msg"] = ex.Message });
            }
        }
    }
}

