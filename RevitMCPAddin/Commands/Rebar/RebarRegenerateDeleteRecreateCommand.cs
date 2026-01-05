// ================================================================
// Command: rebar_regenerate_delete_recreate
// Purpose: Delete tool-generated rebars in host(s) and recreate from
//          current host/mapping values, then update recipe ledger.
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// Kind   : write (high risk)
// ================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.Core.Rebar;

namespace RevitMCPAddin.Commands.Rebar
{
    [RpcCommand("rebar_regenerate_delete_recreate",
        Category = "Rebar",
        Kind = "write",
        Risk = RiskLevel.High,
        Summary = "Delete tool-generated rebars under the selected host(s), recreate from current parameters, and update the in-model recipe ledger.",
        ExampleJsonRpc = "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"rebar_regenerate_delete_recreate\", \"params\":{ \"useSelectionIfEmpty\":true, \"profile\":\"default\", \"tag\":\"RevitMcp:AutoRebar\" } }"
    )]
    public sealed class RebarRegenerateDeleteRecreateCommand : IRevitCommandHandler
    {
        public string CommandName => "rebar_regenerate_delete_recreate";

        private const string DefaultTag = "RevitMcp:AutoRebar";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。", "NO_DOC");

            var p = cmd.Params as JObject ?? new JObject();

            var tag = (p.Value<string>("tag") ?? string.Empty).Trim();
            if (tag.Length == 0)
            {
                var opts = p["options"] as JObject;
                tag = (opts != null ? (opts.Value<string>("tagComments") ?? string.Empty) : string.Empty).Trim();
            }
            if (tag.Length == 0) tag = DefaultTag;

            var deleteMode = (p.Value<string>("deleteMode") ?? "tagged_only").Trim().ToLowerInvariant();
            if (deleteMode.Length == 0) deleteMode = "tagged_only";
            if (deleteMode != "tagged_only")
                return ResultUtil.Err("Unsupported deleteMode: " + deleteMode + " (supported: tagged_only)", "INVALID_ARGS");

            // Build the plan (read-only) first. The recipe signature is derived from the plan.
            var planObj = RebarAutoModelService.BuildPlan(uiapp, doc, p) as JObject;
            if (planObj == null) return ResultUtil.Err("Plan build failed.", "UNKNOWN");
            if (planObj.Value<bool?>("ok") != true) return planObj;

            // Ensure ledger exists (creates DataStorage if missing).
            if (!RebarRecipeLedgerStorage.TryEnsureLedger(doc, out var storage, out var ledger, out var ledgerErr))
                return ResultUtil.Err("Ledger ensure failed: " + ledgerErr, "LEDGER_ENSURE_FAILED");

            var planHosts = planObj["hosts"] as JArray;
            if (planHosts == null) return ResultUtil.Err("plan.hosts is required.", "INVALID_ARGS");

            var results = new JArray();

            foreach (var hTok in planHosts.OfType<JObject>())
            {
                int hid = hTok.Value<int?>("hostElementId") ?? 0;
                var host = hid > 0 ? doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(hid)) : null;

                var r = new JObject
                {
                    ["hostElementId"] = hid,
                    ["tag"] = tag
                };

                if (host == null)
                {
                    r["ok"] = false;
                    r["code"] = "HOST_NOT_FOUND";
                    r["msg"] = "Host element not found.";
                    results.Add(r);
                    continue;
                }

                r["hostUniqueId"] = host.UniqueId ?? string.Empty;
                r["categoryBic"] = hTok.Value<string>("categoryBic");

                bool planOk = hTok.Value<bool?>("ok") ?? false;
                if (!planOk)
                {
                    r["ok"] = false;
                    r["code"] = hTok.Value<string>("code") ?? "PLAN_NOT_OK";
                    r["msg"] = hTok.Value<string>("msg") ?? "Plan is not OK for this host.";
                    results.Add(r);
                    continue;
                }

                var actionsArr = hTok["actions"] as JArray ?? new JArray();

                using (var tx = new Transaction(doc, "RevitMcp - Rebar Regenerate Host " + hid))
                {
                    tx.Start();
                    try
                    {
                        // Delete tagged rebars in this host.
                        var toDelete = RebarDeleteService.CollectTaggedRebarIdsInHost(doc, host, tag);
                        var deleted = RebarDeleteService.DeleteElementsByIds(doc, toDelete);

                        // Recreate from plan.
                        var createdInfos = RebarAutoModelService.ExecuteActionsInTransaction(doc, host, actionsArr, out var layoutWarnings);
                        var createdIds = createdInfos.Select(x => x.elementId).Where(x => x > 0).Distinct().OrderBy(x => x).ToList();

                        int createdMainBars = createdInfos.Count(x => x != null && (x.style ?? string.Empty).Equals("Standard", StringComparison.OrdinalIgnoreCase));
                        int createdStirrupsSets = createdInfos.Count(x => x != null && (x.style ?? string.Empty).Equals("StirrupTie", StringComparison.OrdinalIgnoreCase));

                        // Build recipe + signature.
                        var recipe = RebarRecipeService.BuildRecipe(doc, host, planObj, hTok, p, out var sig);

                        // Persist ledger record (in the same TX as create/delete).
                        var record = new RebarRecipeLedgerHostRecord
                        {
                            engineVersion = RebarRecipeService.EngineVersion,
                            hostUniqueId = host.UniqueId ?? string.Empty,
                            hostElementId = host.Id.IntValue(),
                            hostCategoryBic = hTok.Value<string>("categoryBic") ?? string.Empty,
                            profile = ((hTok["mapping"] as JObject)?["mapping"] as JObject)?.Value<string>("profile") ?? string.Empty,
                            signatureSha256 = sig ?? string.Empty,
                            createdUtc = DateTime.UtcNow.ToString("o"),
                            summary = new
                            {
                                deletedCount = deleted.Count,
                                createdTotal = createdIds.Count,
                                createdMainBars = createdMainBars,
                                createdStirrupsSets = createdStirrupsSets
                            },
                            // Keep recipe snapshot opt-in to avoid bloating the RVT
                            recipeSnapshot = (p.Value<bool?>("storeRecipeSnapshot") ?? false) ? recipe : null
                        };

                        RebarRecipeLedgerStorage.UpsertHostRecord(ledger, record);
                        if (!RebarRecipeLedgerStorage.TryWriteAll(doc, storage, ledger, out var writeErr))
                            throw new InvalidOperationException("Ledger write failed: " + writeErr);

                        tx.Commit();

                        r["ok"] = true;
                        r["deletedRebarIds"] = new JArray(deleted);
                        r["createdRebarIds"] = new JArray(createdIds);
                        r["signatureSha256"] = sig;
                        if (layoutWarnings.Count > 0) r["layoutWarnings"] = layoutWarnings;
                        results.Add(r);
                    }
                    catch (Exception ex)
                    {
                        try { tx.RollBack(); } catch { /* ignore */ }
                        r["ok"] = false;
                        r["code"] = "TX_FAILED";
                        r["msg"] = ex.Message;
                        results.Add(r);
                    }
                }
            }

            return new JObject
            {
                ["ok"] = true,
                ["ledger"] = new JObject
                {
                    ["schemaGuid"] = RebarRecipeLedgerStorage.SchemaGuidString,
                    ["dataStorageId"] = storage.Id.IntValue()
                },
                ["planVersion"] = planObj.Value<int?>("planVersion") ?? 0,
                ["mappingStatus"] = planObj["mappingStatus"],
                ["results"] = results
            };
        }
    }
}

