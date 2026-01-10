// ================================================================
// File: RevitMCPAddin/Commands/MetaOps/DescribeCommandHandler.cs
// Desc: JSON-RPC "describe_command" / "help.describe_command"
//       CommandMetadataRegistry から単一コマンドのメタ情報を返す
// Target: .NET Framework 4.8 / C# 8.0
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.MetaOps
{
    internal sealed class DescribeParams
    {
        public string? method { get; set; }
        public string? name { get; set; }
        public string? command { get; set; }
    }

    [RpcCommand("describe_command",
        Aliases = new[] { "help.describe_command" },
        Category = "MetaOps",
        Tags = new[] { "help", "discovery" },
        Risk = RiskLevel.Low,
        Summary = "Describe a command: returns metadata such as kind/category/risk/tags/aliases.",
        ExampleJsonRpc = "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"describe_command\", \"params\":{ \"method\":\"get_walls\" } }")]
    public sealed class DescribeCommandHandler : IRevitCommandHandler
    {
        public string CommandName => "describe_command";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var p = (cmd.Params as JObject) != null
                    ? ((JObject)cmd.Params!).ToObject<DescribeParams>() ?? new DescribeParams()
                    : new DescribeParams();

                var method = (p?.method ?? p?.name ?? p?.command ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(method))
                    return RpcResultEnvelope.Fail(code: "INVALID_PARAMS", msg: "Missing 'method' (or 'name').");

                if (!CommandMetadataRegistry.TryGet(method, out var meta))
                {
                    var fail = RpcResultEnvelope.Fail(code: "UNKNOWN_COMMAND", msg: "Unknown command: " + method);
                    fail["data"] = new JObject { ["method"] = method };
                    fail["nextActions"] = new JArray
                    {
                        new JObject { ["method"] = "help.search_commands", ["reason"] = "Search for similar commands." },
                        new JObject { ["method"] = "list_commands", ["reason"] = "List available commands and retry with a valid name." }
                    };
                    return fail;
                }

                // Parse exampleJsonRpc -> paramsExample for agent-friendly use.
                JToken paramsExample = ExtractParamsExample(meta.exampleJsonRpc);

                var data = JObject.FromObject(meta);
                data["paramsExample"] = paramsExample;
                data["paramsSchema"] = BuildParamsSchema(meta, paramsExample);
                data["resultSchema"] = BuildLooseObjectSchema();
                data["commonErrorCodes"] = BuildCommonErrorCodes();
                data["requestedMethod"] = method;
                data["resolvedMethod"] = meta.name;
                data["resolvedFromAlias"] = !string.Equals(method, meta.name, StringComparison.OrdinalIgnoreCase);
                data["paramHints"] = BuildParamHints(meta.name, meta.tags);

                // Optional terminology hints (data-driven).
                // Useful for disambiguation such as: 断面(=vertical section) vs 平断面(=plan).
                if (TermMapService.TryGetCommandLexicon(meta.name, out var lex))
                {
                    var st = TermMapService.GetStatus();
                    data["terminology"] = new JObject
                    {
                        ["term_map_version"] = st.term_map_version,
                        ["synonyms"] = new JArray(lex.synonyms ?? Array.Empty<string>()),
                        ["negative_terms"] = new JArray(lex.negative_terms ?? Array.Empty<string>()),
                        ["sources"] = new JArray(lex.sources ?? Array.Empty<string>())
                    };
                }

                return new JObject
                {
                    ["ok"] = true,
                    ["code"] = "OK",
                    ["msg"] = "Command description",
                    ["data"] = data
                };
            }
            catch (Exception ex)
            {
                RevitLogger.Error("describe_command failed: " + ex);
                return RpcResultEnvelope.Fail(code: "INTERNAL_ERROR", msg: ex.Message);
            }
        }

        private static JObject BuildLooseObjectSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = true
            };
        }

        private static JToken ExtractParamsExample(string exampleJsonRpc)
        {
            try
            {
                var s = (exampleJsonRpc ?? string.Empty).Trim();
                if (s.Length == 0) return new JObject();
                var obj = JObject.Parse(s);
                var p = obj["params"];
                return p ?? new JObject();
            }
            catch
            {
                return new JObject();
            }
        }

        private static JObject BuildParamsSchema(RpcCommandMeta meta, JToken paramsExample)
        {
            var schema = BuildLooseObjectSchema();
            if (meta == null) return schema;

            // Supports a small DSL in meta.requires:
            // - "foo"           -> required foo
            // - "a|b|c"         -> requires any of a/b/c (one-of group)
            // This keeps the attribute simple while enabling robust schemas.
            var requires = (meta.requires ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => (x ?? string.Empty).Trim())
                .Where(x => x.Length > 0)
                .ToList();

            var requiredPlain = new List<string>();
            var anyOfGroups = new List<string[]>();

            foreach (var r in requires)
            {
                if (r.IndexOf('|') >= 0)
                {
                    var parts = r.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => (x ?? string.Empty).Trim())
                        .Where(x => x.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    if (parts.Length == 1) requiredPlain.Add(parts[0]);
                    else if (parts.Length > 1) anyOfGroups.Add(parts);
                }
                else
                {
                    requiredPlain.Add(r);
                }
            }

            // Also include example keys as hints (not required).
            var exampleKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (paramsExample is JObject jo)
                {
                    foreach (var prop in jo.Properties())
                    {
                        if (prop == null) continue;
                        var n = (prop.Name ?? string.Empty).Trim();
                        if (n.Length > 0) exampleKeys.Add(n);
                    }
                }
            }
            catch { /* ignore */ }

            var propsObj = new JObject();
            void EnsureProp(string name)
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                if (propsObj.ContainsKey(name)) return;
                propsObj[name] = new JObject
                {
                    ["type"] = new JArray("string", "number", "integer", "object", "array", "boolean", "null")
                };
            }

            foreach (var r in requiredPlain) EnsureProp(r);
            foreach (var g in anyOfGroups) foreach (var r in g) EnsureProp(r);
            foreach (var k in exampleKeys) EnsureProp(k);

            if (propsObj.Count > 0) schema["properties"] = propsObj;

            // Build required/allOf/anyOf constraints.
            var allOf = new JArray();
            if (requiredPlain.Count > 0)
                allOf.Add(new JObject { ["required"] = new JArray(requiredPlain.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()) });

            foreach (var group in anyOfGroups)
            {
                var anyOf = new JArray();
                foreach (var alt in group)
                    anyOf.Add(new JObject { ["required"] = new JArray(alt) });
                if (anyOf.Count > 0) allOf.Add(new JObject { ["anyOf"] = anyOf });
            }

            if (allOf.Count > 0) schema["allOf"] = allOf;
            return schema;
        }

        private static JArray BuildParamHints(string method, string[] tags)
        {
            var hints = new List<string>();
            var m = (method ?? string.Empty).Trim();
            var ml = m.ToLowerInvariant();

            if (ml.StartsWith("element."))
                hints.AddRange(new[] { "elementId", "elementIds", "uniqueId" });
            else if (ml.StartsWith("view."))
                hints.AddRange(new[] { "viewId", "viewName" });
            else if (ml.StartsWith("sheet."))
                hints.AddRange(new[] { "sheetId", "sheetNumber" });
            else if (ml.StartsWith("viewport."))
                hints.AddRange(new[] { "viewportId" });
            else if (ml.StartsWith("doc."))
                hints.AddRange(new[] { "docPathHint" });

            // Some commands use selection as input; remind it explicitly.
            if (ml.IndexOf("selection", StringComparison.OrdinalIgnoreCase) >= 0 ||
                ml.IndexOf("selected", StringComparison.OrdinalIgnoreCase) >= 0)
                hints.Add("Uses current selection when ids are omitted");

            // Add tags as hints for search/mental model (best-effort).
            if (tags != null)
            {
                foreach (var t in tags)
                {
                    var tt = (t ?? string.Empty).Trim();
                    if (tt.Length > 0 && !hints.Contains(tt, StringComparer.OrdinalIgnoreCase))
                        hints.Add(tt);
                }
            }

            return new JArray(hints.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }

        private static JArray BuildCommonErrorCodes()
        {
            // Keep it short and stable; agents can use this as a hint list.
            return new JArray
            {
                new JObject { ["code"] = "OK", ["msg"] = "Success" },
                new JObject { ["code"] = "INVALID_PARAMS", ["msg"] = "Missing/invalid parameters" },
                new JObject { ["code"] = "UNKNOWN_COMMAND", ["msg"] = "No such command" },
                new JObject { ["code"] = "NOT_READY", ["msg"] = "Metadata/registry not ready" },
                new JObject { ["code"] = "PRECONDITION_FAILED", ["msg"] = "Revit state/selection/view precondition not met" },
                new JObject { ["code"] = "REVIT_BUSY", ["msg"] = "Revit is busy; retry later" },
                new JObject { ["code"] = "TIMEOUT", ["msg"] = "Timed out while executing" },
                new JObject { ["code"] = "INTERNAL_ERROR", ["msg"] = "Unhandled exception or internal error" }
            };
        }
    }
}
