using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Rhino.FileIO;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace RhinoMcpServer.Rpc.Tools
{
    public static class Merge3dmCommand
    {
        // params: { "inputs": ["a.3dm","b.3dm",...], "dst": "out.3dm", "version": 7 }
        public static Task<object> HandleAsync(JObject p)
        {
            var arr = p["inputs"] as JArray;
            if (arr == null || arr.Count == 0)
                return Task.FromResult<object>(new JObject { ["ok"] = false, ["msg"] = "inputs required" });

            var inputs = arr.Values<string>().Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            foreach (var path in inputs) { if (!File.Exists(path)) return Task.FromResult<object>(new JObject { ["ok"] = false, ["msg"] = $"missing: {path}" }); }

            string dst = (string)(p["dst"] ?? "");
            int version = (int?)p["version"] ?? 7;
            if (string.IsNullOrWhiteSpace(dst)) dst = inputs[0];

            try
            {
                var outFile = new File3dm();
                // Initialize units from first file
                var f0 = File3dm.Read(inputs[0]);
                if (f0 != null)
                {
                    outFile.Settings.ModelUnitSystem = f0.Settings.ModelUnitSystem;
                }

                // helper to ensure layer
                var layerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int EnsureLayer(File3dm f, int layerIndex)
                {
                    if (layerIndex < 0 || layerIndex >= f.AllLayers.Count) return -1;
                    var srcLayer = System.Linq.Enumerable.ElementAt(f.AllLayers, layerIndex);
                    var name = string.IsNullOrWhiteSpace(srcLayer.FullPath) ? srcLayer.Name : srcLayer.FullPath;
                    if (!layerMap.TryGetValue(name, out int dstIndex))
                    {
                        var newLayer = new Layer { Name = name };
                        outFile.AllLayers.Add(newLayer);
                        dstIndex = outFile.AllLayers.Count - 1;
                        layerMap[name] = dstIndex;
                    }
                    return dstIndex;
                }

                int objCount = 0;
                foreach (var path in inputs)
                {
                    var f = File3dm.Read(path);
                    if (f == null) continue;
                    foreach (var it in f.Objects)
                    {
                        var g = it.Geometry;
                        var attr = it.Attributes ?? new ObjectAttributes();
                        // remap layer
                        attr.LayerIndex = EnsureLayer(f, attr.LayerIndex);
                        if (g != null)
                        {
                            if (g is Mesh m) outFile.Objects.AddMesh(m, attr);
                            else if (g is Brep b) outFile.Objects.AddBrep(b, attr);
                            else if (g is Curve c) outFile.Objects.AddCurve(c, attr);
                            else if (g is Point pnt) outFile.Objects.AddPoint(pnt.Location, attr);
                            else outFile.Objects.Add(g, attr);
                            objCount++;
                        }
                    }
                }

                var folder = Path.GetDirectoryName(dst);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                if (File.Exists(dst)) File.Delete(dst);
                outFile.Write(dst, version);
                return Task.FromResult<object>(new JObject { ["ok"] = true, ["dst"] = dst, ["objects"] = objCount, ["version"] = version });
            }
            catch (Exception ex)
            {
                return Task.FromResult<object>(new JObject { ["ok"] = false, ["msg"] = ex.Message });
            }
        }
    }
}
