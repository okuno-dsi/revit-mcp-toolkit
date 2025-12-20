using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System;

namespace RhinoMcpServer.Rpc.Rhino
{
    public static class ImportByIdsCommand
    {
        // params: { "uniqueIds": ["id1","id2",...], "revitBaseUrl": "http://127.0.0.1:5210" }
        public static async Task<object> HandleAsync(JObject p)
        {
            var ids = p["uniqueIds"] as JArray;
            if (ids == null || ids.Count == 0)
                return new JObject { ["ok"] = false, ["msg"] = "uniqueIds required" };

            string revitBase = (string)(p["revitBaseUrl"] ?? "http://127.0.0.1:5210");

            int okCount = 0; int errCount = 0;
            foreach (var id in ids)
            {
                var call = new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ["method"] = "get_instance_geometry",
                    ["params"] = new JObject { ["uniqueId"] = id }
                };
                try
                {
                    var resText = await RevitProxy.RevitMcpClient.PostRpcAndWaitAsync(revitBase, call.ToString());
                    var jo = JObject.Parse(resText);
                    var result = jo["result"] as JObject;
                    // descend nested result if present (JSON-RPC wrapper -> action wrapper)
                    for (int i = 0; i < 3 && result != null; i++)
                    {
                        var next = result["result"] as JObject;
                        if (next != null) result = next; else break;
                    }

                    // Fallback: if no result (or Revit MCP lacks get_instance_geometry), synthesize a bbox mesh via get_element_info
                    if (result == null || result["ok"]?.Value<bool>() == false)
                    {
                        // Try get_element_info rich=true to obtain bboxMm
                        var infoCall = new JObject
                        {
                            ["jsonrpc"] = "2.0",
                            ["id"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            ["method"] = "get_element_info",
                            ["params"] = new JObject { ["uniqueId"] = id, ["rich"] = true }
                        };
                        var infoText = await RevitProxy.RevitMcpClient.PostRpcAndWaitAsync(revitBase, infoCall.ToString());
                        var infoJo = JObject.Parse(infoText);
                        var infoLeaf = infoJo["result"]?["result"] as JObject ?? infoJo["result"] as JObject;
                        var elements = infoLeaf?["elements"] as JArray;
                        var bbox = elements != null && elements.Count > 0 ? elements[0]?["bboxMm"] as JObject : null;
                        if (bbox != null)
                        {
                            var min = bbox["min"] as JObject;
                            var max = bbox["max"] as JObject;
                            if (min != null && max != null)
                            {
                                double mm_to_ft = 1.0 / 304.8;
                                double minx = min.Value<double>("x") * mm_to_ft;
                                double miny = min.Value<double>("y") * mm_to_ft;
                                double minz = min.Value<double>("z") * mm_to_ft;
                                double maxx = max.Value<double>("x") * mm_to_ft;
                                double maxy = max.Value<double>("y") * mm_to_ft;
                                double maxz = max.Value<double>("z") * mm_to_ft;

                                // 8 vertices of the box (feet)
                                var verts = new JArray
                                {
                                    new JArray(minx, miny, minz),
                                    new JArray(maxx, miny, minz),
                                    new JArray(maxx, maxy, minz),
                                    new JArray(minx, maxy, minz),
                                    new JArray(minx, miny, maxz),
                                    new JArray(maxx, miny, maxz),
                                    new JArray(maxx, maxy, maxz),
                                    new JArray(minx, maxy, maxz)
                                };
                                // 12 triangles (two per face)
                                var idx = new JArray
                                {
                                    0,1,2, 0,2,3, // bottom
                                    4,5,6, 4,6,7, // top
                                    0,1,5, 0,5,4, // front
                                    1,2,6, 1,6,5, // right
                                    2,3,7, 2,7,6, // back
                                    3,0,4, 3,4,7  // left
                                };
                                result = new JObject
                                {
                                    ["uniqueId"] = id,
                                    ["units"] = "feet",
                                    ["vertices"] = verts,
                                    ["submeshes"] = new JArray(new JObject { ["materialKey"] = "bbox", ["intIndices"] = idx }),
                                    ["snapshotStamp"] = DateTime.UtcNow.ToString("o")
                                };
                            }
                        }
                    }

                    if (result == null)
                    {
                        errCount++;
                        continue;
                    }

                    // Normalize indices naming for plugin tolerance
                    var subs = result["submeshes"] as JArray;
                    if (subs != null)
                    {
                        foreach (var sm in subs)
                        {
                            if (sm is JObject so)
                            {
                                if (so["intIndices"] == null && so["indices"] is JArray idx)
                                    so["intIndices"] = idx;
                            }
                        }
                    }

                    // Forward to plugin importer
                    var imp = new JObject
                    {
                        ["jsonrpc"] = "2.0",
                        ["id"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        ["method"] = "rhino_import_snapshot",
                        ["params"] = result
                    };
                    var impResText = await PluginIpcClient.PostAsync(imp.ToString());
                    var impJo = JObject.Parse(impResText);
                    if (impJo["result"]?["ok"]?.Value<bool>() == true) okCount++; else errCount++;
                }
                catch
                {
                    errCount++;
                }
            }

            return new JObject { ["ok"] = errCount == 0, ["imported"] = okCount, ["errors"] = errCount };
        }
    }
}
