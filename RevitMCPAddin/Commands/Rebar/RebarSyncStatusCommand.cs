// ================================================================
// Command: rebar_sync_status
// Purpose: Compare current auto-rebar "recipe signature" vs the last
//          recorded signature in the in-model recipe ledger.
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// Kind   : read
// ================================================================
using System;
using System.Linq;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.Core.Rebar;

namespace RevitMCPAddin.Commands.Rebar
{
    [RpcCommand("rebar_sync_status",
        Category = "Rebar",
        Kind = "read",
        Risk = RiskLevel.Low,
        Summary = "Check whether selected rebar hosts are in sync with the last delete&recreate run (recipe ledger signature).",
        ExampleJsonRpc = "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"rebar_sync_status\", \"params\":{ \"useSelectionIfEmpty\":true, \"profile\":\"default\" } }"
    )]
    public sealed class RebarSyncStatusCommand : IRevitCommandHandler
    {
        public string CommandName => "rebar_sync_status";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。", "NO_DOC");

            var p = cmd.Params as JObject ?? new JObject();

            // Build current plan (read-only) so the signature reflects the actual planned geometry/options.
            var planObj = RebarAutoModelService.BuildPlan(uiapp, doc, p) as JObject;
            if (planObj == null) return ResultUtil.Err("Plan build failed.", "UNKNOWN");
            if (planObj.Value<bool?>("ok") != true) return planObj;

            if (!RebarRecipeLedgerStorage.TryReadAll(doc, out var hasLedger, out var dsId, out var ledger, out var ledgerErr))
                return ResultUtil.Err("Ledger read failed: " + ledgerErr, "LEDGER_READ_FAILED");

            var hosts = new JArray();
            var planHosts = planObj["hosts"] as JArray;
            if (planHosts != null)
            {
                foreach (var hTok in planHosts.OfType<JObject>())
                {
                    int hid = hTok.Value<int?>("hostElementId") ?? 0;
                    var host = hid > 0 ? doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(hid)) : null;

                    var r = new JObject
                    {
                        ["hostElementId"] = hid,
                        ["planOk"] = hTok.Value<bool?>("ok") ?? false,
                        ["planCode"] = hTok.Value<string>("code"),
                        ["planMsg"] = hTok.Value<string>("msg")
                    };

                    if (host == null)
                    {
                        r["ok"] = false;
                        r["code"] = "HOST_NOT_FOUND";
                        r["msg"] = "Host element not found.";
                        hosts.Add(r);
                        continue;
                    }

                    r["hostUniqueId"] = host.UniqueId ?? string.Empty;
                    r["categoryBic"] = hTok.Value<string>("categoryBic");
                    r["categoryName"] = hTok.Value<string>("categoryName");

                    if ((hTok.Value<bool?>("ok") ?? false) != true)
                    {
                        r["ok"] = false;
                        r["code"] = "PLAN_NOT_OK";
                        r["msg"] = "Plan is not OK for this host.";
                        hosts.Add(r);
                        continue;
                    }

                    var recipe = RebarRecipeService.BuildRecipe(doc, host, planObj, hTok, p, out var currentSig);

                    string lastSig = string.Empty;
                    string lastRunUtc = string.Empty;
                    object lastSummary = null;
                    string lastProfile = string.Empty;

                    if (hasLedger && RebarRecipeLedgerStorage.TryGetHostRecord(ledger, host.UniqueId, out var rec) && rec != null)
                    {
                        lastSig = rec.signatureSha256 ?? string.Empty;
                        lastRunUtc = rec.createdUtc ?? string.Empty;
                        lastSummary = rec.summary;
                        lastProfile = rec.profile ?? string.Empty;
                    }

                    bool inSync = (!string.IsNullOrWhiteSpace(lastSig) && !string.IsNullOrWhiteSpace(currentSig)
                        && string.Equals(lastSig, currentSig, StringComparison.OrdinalIgnoreCase));

                    r["ok"] = true;
                    r["hasLedger"] = hasLedger;
                    r["ledgerDataStorageId"] = dsId;
                    r["lastSignature"] = lastSig;
                    r["currentSignature"] = currentSig;
                    r["isInSync"] = inSync;
                    r["lastRunUtc"] = lastRunUtc;
                    r["lastProfile"] = lastProfile.Length > 0 ? lastProfile : null;
                    if (lastSummary != null) r["lastSummary"] = JToken.FromObject(lastSummary);

                    // Optional: include the computed recipe for debugging (opt-in)
                    if ((p.Value<bool?>("includeRecipe") ?? false) == true)
                        r["currentRecipe"] = recipe;

                    hosts.Add(r);
                }
            }

            return new JObject
            {
                ["ok"] = true,
                ["ledger"] = new JObject
                {
                    ["schemaGuid"] = RebarRecipeLedgerStorage.SchemaGuidString,
                    ["hasLedger"] = hasLedger,
                    ["dataStorageId"] = dsId
                },
                ["planVersion"] = planObj.Value<int?>("planVersion") ?? 0,
                ["mappingStatus"] = planObj["mappingStatus"],
                ["hosts"] = hosts
            };
        }
    }
}
