// ================================================================
// File: Commands/MetaOps/GetMcpLedgerSummaryCommand.cs
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8.0
// JSON-RPC: get_mcp_ledger_summary
// Purpose : Read (and optionally create) MCP ledger stored in DataStorage.
// ================================================================
#nullable enable
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.MetaOps
{
    public sealed class GetMcpLedgerSummaryCommand : IRevitCommandHandler
    {
        public string CommandName => "get_mcp_ledger_summary";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = cmd.Params as JObject ?? new JObject();
            bool createIfMissing = p.Value<bool?>("createIfMissing") ?? true;

            return McpLedgerEngine.GetLedgerSummary(uiapp, createIfMissing);
        }
    }
}

