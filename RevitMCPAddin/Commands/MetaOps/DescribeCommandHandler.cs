// ================================================================
// File: RevitMCPAddin/Commands/MetaOps/DescribeCommandHandler.cs
// Desc: JSON-RPC "describe_command" / "help.describe_command"
//       CommandMetadataRegistry から単一コマンドのメタ情報を返す
// Target: .NET Framework 4.8 / C# 8.0
// ================================================================
#nullable enable
using System;
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

                var data = JObject.FromObject(meta);
                data["paramsSchema"] = BuildLooseObjectSchema();
                data["resultSchema"] = BuildLooseObjectSchema();
                data["commonErrorCodes"] = BuildCommonErrorCodes();

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
