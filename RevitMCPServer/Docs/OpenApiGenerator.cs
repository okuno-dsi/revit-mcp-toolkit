// File: RevitMcpServer/Docs/OpenApiGenerator.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.IO;
using RevitMCP.Abstractions.Rpc;

namespace RevitMcpServer.Docs
{
    public static class OpenApiGenerator
    {
        public static string Generate(RpcRouter router) => Generate(router, new List<DocMethod>());

        public static string Generate(RpcRouter router, IEnumerable<DocMethod> extras)
        {
            var root = new Dictionary<string, object?>
            {
                ["openapi"] = "3.1.0",
                ["info"] = new Dictionary<string, object?>
                {
                    ["title"] = "Revit MCP Server API",
                    ["version"] = "1.0.0",
                    ["description"] = "Auto-generated OpenAPI for RPC methods (virtual paths)"
                },
                ["paths"] = new Dictionary<string, object?>(),
                ["components"] = new Dictionary<string, object?>
                {
                    ["schemas"] = new Dictionary<string, object?>()
                }
            };

            var paths = (Dictionary<string, object?>)root["paths"]!;
            var schemas = (Dictionary<string, object?>)((Dictionary<string, object?>)root["components"]!)["schemas"]!;
            EnsureCommonSchemas(schemas);

            // Router 由来（サーバ内/Abstractions 側）
            foreach (var kv in router.GetAllCommands())
            {
                var method = kv.Key;
                var cmd = kv.Value;
                var resultRef = BuildResponseSchema(method, cmd.ResultType, schemas);
                AddRpcPath(paths, schemas, method, resultRef, null);
            }

            // Add-in マニフェスト由来（tags を OpenAPI にも反映）
            foreach (var m in extras)
            {
                var resultRef = BuildResponseSchemaFromDict(m.Name, m.ResultSchema, schemas);
                AddRpcPath(paths, schemas, m.Name, resultRef, m.Tags ?? new string[0]);
            }

            // 非RPC 経路（ポーリング・メタ）
            AddNonRpcPaths(paths, schemas);

            return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
        }

        public static void GenerateToFiles(RpcRouter router, string outputDir, IEnumerable<DocMethod> extras)
        {
            Directory.CreateDirectory(outputDir);
            var json = Generate(router, extras);
            AtomicWrite(Path.Combine(outputDir, "openapi.json"), json);
        }

        // ---------- 内部: パス生成 ----------
        private static void AddRpcPath(Dictionary<string, object?> paths, Dictionary<string, object?> schemas,
                                       string method, Dictionary<string, object?> resultSchemaRef, string[]? tags)
        {
            string path = "/rpc/" + method;
            var op = new Dictionary<string, object?>
            {
                ["summary"] = null,
                ["tags"] = tags ?? new string[0],
                ["requestBody"] = new Dictionary<string, object?>
                {
                    ["required"] = true,
                    ["content"] = new Dictionary<string, object?>
                    {
                        ["application/json"] = new Dictionary<string, object?>
                        {
                            ["schema"] = BuildRequestSchema(method, schemas)
                        }
                    }
                },
                ["responses"] = new Dictionary<string, object?>
                {
                    ["200"] = new Dictionary<string, object?>
                    {
                        ["description"] = "OK",
                        ["content"] = new Dictionary<string, object?>
                        {
                            ["application/json"] = new Dictionary<string, object?> { ["schema"] = resultSchemaRef }
                        }
                    },
                    ["400"] = new Dictionary<string, object?>
                    {
                        ["description"] = "JSON-RPC error",
                        ["content"] = new Dictionary<string, object?>
                        {
                            ["application/json"] = new Dictionary<string, object?> { ["schema"] = new Dictionary<string, object?> { ["$ref"] = "#/components/schemas/JsonRpcError" } }
                        }
                    },
                    ["500"] = new Dictionary<string, object?>
                    {
                        ["description"] = "Server error",
                        ["content"] = new Dictionary<string, object?>
                        {
                            ["application/json"] = new Dictionary<string, object?> { ["schema"] = new Dictionary<string, object?> { ["$ref"] = "#/components/schemas/JsonRpcError" } }
                        }
                    }
                }
            };
            paths[path] = new Dictionary<string, object?> { ["post"] = op };
        }

        private static object BuildRequestSchema(string method, Dictionary<string, object?> schemas)
        {
            var name = method + "_Request";
            schemas[name] = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["jsonrpc"] = new Dictionary<string, object?> { ["type"] = "string", ["example"] = "2.0" },
                    ["method"] = new Dictionary<string, object?> { ["type"] = "string", ["example"] = method },
                    ["params"] = new Dictionary<string, object?> { ["type"] = "object" },
                    ["id"] = new Dictionary<string, object?>
                    {
                        ["oneOf"] = new object[] {
                        new Dictionary<string, object?> { ["type"] = "integer" },
                        new Dictionary<string, object?> { ["type"] = "string" } }
                    }
                },
                ["required"] = new[] { "jsonrpc", "method" }
            };
            return new Dictionary<string, object?> { ["$ref"] = "#/components/schemas/" + name };
        }

        private static Dictionary<string, object?> BuildResponseSchema(string method, Type? resultType, Dictionary<string, object?> schemas)
        {
            var name = method + "_Response";
            var resSchema = resultType != null ? SchemaUtils.ToJsonSchema(resultType)
                                               : new Dictionary<string, object?> { ["type"] = "object" };
            schemas[name] = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["jsonrpc"] = new Dictionary<string, object?> { ["type"] = "string", ["example"] = "2.0" },
                    ["result"] = resSchema,
                    ["id"] = new Dictionary<string, object?>
                    {
                        ["oneOf"] = new object[] {
                        new Dictionary<string, object?> { ["type"] = "integer" },
                        new Dictionary<string, object?> { ["type"] = "string" } }
                    }
                },
                ["required"] = new[] { "jsonrpc" }
            };
            return new Dictionary<string, object?> { ["$ref"] = "#/components/schemas/" + name };
        }

        private static Dictionary<string, object?> BuildResponseSchemaFromDict(string method, Dictionary<string, object?>? resultDict, Dictionary<string, object?> schemas)
        {
            var name = method + "_Response";
            var resSchema = resultDict ?? new Dictionary<string, object?> { ["type"] = "object" };
            schemas[name] = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["jsonrpc"] = new Dictionary<string, object?> { ["type"] = "string", ["example"] = "2.0" },
                    ["result"] = resSchema,
                    ["id"] = new Dictionary<string, object?>
                    {
                        ["oneOf"] = new object[] {
                        new Dictionary<string, object?> { ["type"] = "integer" },
                        new Dictionary<string, object?> { ["type"] = "string" } }
                    }
                },
                ["required"] = new[] { "jsonrpc" }
            };
            return new Dictionary<string, object?> { ["$ref"] = "#/components/schemas/" + name };
        }

        private static void EnsureCommonSchemas(Dictionary<string, object?> schemas)
        {
            if (!schemas.ContainsKey("JsonRpcError"))
            {
                schemas["JsonRpcError"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["jsonrpc"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = new[] { "2.0" } },
                        ["error"] = new Dictionary<string, object?>
                        {
                            ["type"] = "object",
                            ["properties"] = new Dictionary<string, object?>
                            {
                                ["code"] = new Dictionary<string, object?> { ["type"] = "integer" },
                                ["message"] = new Dictionary<string, object?> { ["type"] = "string" },
                                ["data"] = new Dictionary<string, object?> { ["type"] = "object" }
                            },
                            ["required"] = new[] { "code", "message" }
                        },
                        ["id"] = new Dictionary<string, object?>
                        {
                            ["oneOf"] = new object[] {
                            new Dictionary<string, object?> { ["type"] = "integer" },
                            new Dictionary<string, object?> { ["type"] = "string" } },
                            ["nullable"] = true
                        }
                    },
                    ["required"] = new[] { "jsonrpc", "error" }
                };
            }
            if (!schemas.ContainsKey("JsonRpcRequest"))
            {
                schemas["JsonRpcRequest"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["jsonrpc"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = new[] { "2.0" } },
                        ["method"] = new Dictionary<string, object?> { ["type"] = "string", ["minLength"] = 1 },
                        ["params"] = new Dictionary<string, object?> { ["type"] = "object" },
                        ["id"] = new Dictionary<string, object?>
                        {
                            ["oneOf"] = new object[] {
                            new Dictionary<string, object?> { ["type"] = "integer" },
                            new Dictionary<string, object?> { ["type"] = "string" } }
                        }
                    },
                    ["required"] = new[] { "jsonrpc", "method" }
                };
            }
            if (!schemas.ContainsKey("EnqueueAccepted"))
            {
                schemas["EnqueueAccepted"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["ok"] = new Dictionary<string, object?> { ["type"] = "boolean" },
                        ["commandId"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["mode"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = new[] { "queued", "direct" } }
                    },
                    ["required"] = new[] { "ok" }
                };
            }
        }

        private static void AddNonRpcPaths(Dictionary<string, object?> paths, Dictionary<string, object?> schemas)
        {
            // /enqueue
            paths["/enqueue"] = new Dictionary<string, object?>
            {
                ["post"] = new Dictionary<string, object?>
                {
                    ["summary"] = "Enqueue a JSON-RPC request to be executed in Revit",
                    ["requestBody"] = new Dictionary<string, object?>
                    {
                        ["required"] = true,
                        ["content"] = new Dictionary<string, object?>
                        {
                            ["application/json"] = new Dictionary<string, object?> { ["schema"] = new Dictionary<string, object?> { ["$ref"] = "#/components/schemas/JsonRpcRequest" } }
                        }
                    },
                    ["responses"] = new Dictionary<string, object?>
                    {
                        ["200"] = new Dictionary<string, object?>
                        {
                            ["description"] = "Accepted",
                            ["content"] = new Dictionary<string, object?>
                            {
                                ["application/json"] = new Dictionary<string, object?> { ["schema"] = new Dictionary<string, object?> { ["$ref"] = "#/components/schemas/EnqueueAccepted" } }
                            }
                        },
                        ["409"] = new Dictionary<string, object?> { ["description"] = "Inflight or Duplicate" },
                        ["413"] = new Dictionary<string, object?> { ["description"] = "Payload too large" }
                    }
                }
            };

            // /pending_request
            paths["/pending_request"] = new Dictionary<string, object?>
            {
                ["get"] = new Dictionary<string, object?>
                {
                    ["summary"] = "Fetch next pending JSON-RPC request (and set inflight=ON)",
                    ["responses"] = new Dictionary<string, object?>
                    {
                        ["200"] = new Dictionary<string, object?>
                        {
                            ["description"] = "Next JSON-RPC request",
                            ["content"] = new Dictionary<string, object?>
                            {
                                ["application/json"] = new Dictionary<string, object?> { ["schema"] = new Dictionary<string, object?> { ["$ref"] = "#/components/schemas/JsonRpcRequest" } }
                            }
                        },
                        ["204"] = new Dictionary<string, object?> { ["description"] = "No pending request" }
                    }
                }
            };

            // /post_result
            paths["/post_result"] = new Dictionary<string, object?>
            {
                ["post"] = new Dictionary<string, object?>
                {
                    ["summary"] = "Add-in posts execution result (inflight=OFF)",
                    ["requestBody"] = new Dictionary<string, object?>
                    {
                        ["required"] = true,
                        ["content"] = new Dictionary<string, object?>
                        {
                            ["application/json"] = new Dictionary<string, object?> { ["schema"] = new Dictionary<string, object?> { ["type"] = "object" } }
                        }
                    },
                    ["responses"] = new Dictionary<string, object?>
                    {
                        ["200"] = new Dictionary<string, object?> { ["description"] = "Stored" },
                        ["400"] = new Dictionary<string, object?> { ["description"] = "Invalid result" }
                    }
                }
            };

            // /get_result
            paths["/get_result"] = new Dictionary<string, object?>
            {
                ["get"] = new Dictionary<string, object?>
                {
                    ["summary"] = "Client fetches the latest execution result",
                    ["responses"] = new Dictionary<string, object?>
                    {
                        ["200"] = new Dictionary<string, object?>
                        {
                            ["description"] = "JSON result",
                            ["content"] = new Dictionary<string, object?>
                            {
                                ["application/json"] = new Dictionary<string, object?> { ["schema"] = new Dictionary<string, object?> { ["type"] = "object" } }
                            }
                        },
                        ["204"] = new Dictionary<string, object?> { ["description"] = "No result yet" }
                    }
                }
            };

            // /manifest /manifest/register
            paths["/manifest"] = new Dictionary<string, object?>
            {
                ["get"] = new Dictionary<string, object?>
                {
                    ["summary"] = "Inspect currently registered add-in commands",
                    ["responses"] = new Dictionary<string, object?>
                    {
                        ["200"] = new Dictionary<string, object?> { ["description"] = "OK", ["content"] = new Dictionary<string, object?> { ["application/json"] = new Dictionary<string, object?> { ["schema"] = new Dictionary<string, object?> { ["type"] = "object" } } } }
                    }
                }
            };
            paths["/manifest/register"] = new Dictionary<string, object?>
            {
                ["post"] = new Dictionary<string, object?>
                {
                    ["summary"] = "Register add-in commands manifest",
                    ["requestBody"] = new Dictionary<string, object?>
                    {
                        ["required"] = true,
                        ["content"] = new Dictionary<string, object?>
                        {
                            ["application/json"] = new Dictionary<string, object?> { ["schema"] = new Dictionary<string, object?> { ["type"] = "object" } }
                        }
                    },
                    ["responses"] = new Dictionary<string, object?>
                    {
                        ["200"] = new Dictionary<string, object?> { ["description"] = "Registered" },
                        ["400"] = new Dictionary<string, object?> { ["description"] = "Invalid manifest" }
                    }
                }
            };
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
