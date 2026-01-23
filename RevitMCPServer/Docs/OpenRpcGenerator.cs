// File: RevitMcpServer/Docs/OpenRpcGenerator.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.IO;
using RevitMCP.Abstractions.Rpc;

namespace RevitMcpServer.Docs
{
    public static class OpenRpcGenerator
    {
        public static string Generate(RpcRouter router) => Generate(router, new List<DocMethod>());

        public static string Generate(RpcRouter router, IEnumerable<DocMethod> extras)
        {
            var root = new Dictionary<string, object?>
            {
                ["openrpc"] = "1.2.6",
                ["info"] = new Dictionary<string, object?>
                {
                    ["title"] = "Revit MCP Server",
                    ["version"] = "1.0.0",
                    ["description"] = "Auto-generated OpenRPC document for JSON-RPC methods"
                },
                ["methods"] = new List<object?>()
            };

            var methods = (List<object?>)root["methods"]!;

            // Router 由来
            foreach (var kv in router.GetAllCommands())
            {
                var cmd = kv.Value;
                methods.Add(new Dictionary<string, object?>
                {
                    ["name"] = kv.Key,
                    ["summary"] = null,
                    ["tags"] = new string[0],
                    ["params"] = new List<object?> {
                        new Dictionary<string, object?> {
                            ["name"] = "params",
                            ["schema"] = cmd.ParamsType != null ? SchemaUtils.ToJsonSchema(cmd.ParamsType)
                                                                : new Dictionary<string, object?> { ["type"] = "object" }
                        }
                    },
                    ["result"] = new Dictionary<string, object?>
                    {
                        ["name"] = "result",
                        ["schema"] = cmd.ResultType != null ? SchemaUtils.ToJsonSchema(cmd.ResultType)
                                                            : new Dictionary<string, object?> { ["type"] = "object" }
                    }
                });
            }

            // Add-in マニフェスト 由来
            foreach (var m in extras)
            {
                methods.Add(new Dictionary<string, object?>
                {
                    ["name"] = m.Name,
                    ["summary"] = string.IsNullOrWhiteSpace(m.Summary) ? null : m.Summary,
                    ["tags"] = m.Tags ?? new string[0],
                    ["params"] = new List<object?> {
                        new Dictionary<string, object?> {
                            ["name"] = "params",
                            ["schema"] = m.ParamsSchema ?? new Dictionary<string, object?> { ["type"] = "object" }
                        }
                    },
                    ["result"] = new Dictionary<string, object?>
                    {
                        ["name"] = "result",
                        ["schema"] = m.ResultSchema ?? new Dictionary<string, object?> { ["type"] = "object" }
                    }
                });
            }

            return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
        }

        public static void GenerateToFiles(RpcRouter router, string outputDir, IEnumerable<DocMethod> extras)
        {
            Directory.CreateDirectory(outputDir);
            var json = Generate(router, extras);
            AtomicWrite(Path.Combine(outputDir, "openrpc.json"), json);
        }

        private static void AtomicWrite(string path, string content)
        {
            var dir = Path.GetDirectoryName(path) ?? "";
            Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, content, Encoding.UTF8);

            try
            {
                if (File.Exists(path))
                {
                    File.Replace(tmp, path, null);
                }
                else
                {
                    File.Move(tmp, path);
                }
            }
            catch
            {
                try { File.Copy(tmp, path, true); } catch { }
                try { File.Delete(tmp); } catch { }
                throw;
            }
        }
    }
}
