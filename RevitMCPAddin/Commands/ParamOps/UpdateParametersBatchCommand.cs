using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ParamOps
{
    /// <summary>
    /// Batch parameter updates across elements and element types with time-sliced transactions.
    /// Input:
    /// {
    ///   items: [
    ///     { elementId?:int, typeId?:int, target?:"instance"|"type"|"auto",
    ///       paramName?:string, builtInName?:string, builtInId?:int, guid?:string,
    ///       value:any }
    ///   ],
    ///   startIndex?:int, batchSize?:int, maxMillisPerTx?:int
    /// }
    /// Output: { ok, nextIndex?, completed, total, updatedCount, failedCount, items:[{ok, id, where, param, msg?}] }
    /// </summary>
    public class UpdateParametersBatchCommand : IRevitCommandHandler
    {
        public string CommandName => "update_parameters_batch";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = (JObject)(cmd.Params ?? new JObject());
            var itemsTok = p["items"] as JArray;
            if (itemsTok == null || itemsTok.Count == 0) return new { ok = false, msg = "items[] required" };

            var items = itemsTok.OfType<JObject>().ToList();
            int total = items.Count;
            int start = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
            int batchSize = Math.Max(1, p.Value<int?>("batchSize") ?? Math.Min(400, total));
            int maxMillis = Math.Max(100, p.Value<int?>("maxMillisPerTx") ?? 2500);
            bool suppressItems = p.Value<bool?>("suppressItems") ?? false;

            var slice = items.Skip(start).Take(batchSize).ToList();
            var results = new List<object>(slice.Count);
            int okCount = 0, failCount = 0;

            var tx = new Transaction(doc, "Update Parameters (batch)");
            var started = false;
            var startAt = DateTime.UtcNow;
            try
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                started = true;

                foreach (var it in slice)
                {
                    try
                    {
                        string target = (it.Value<string>("target") ?? "auto").Trim().ToLowerInvariant();

                        Element e = null;
                        if (target == "type")
                        {
                            var tid = it.Value<int?>("typeId") ?? 0;
                            if (tid <= 0) throw new InvalidOperationException("typeId required for target=type");
                            e = doc.GetElement(new ElementId(tid)) as ElementType;
                            if (e == null) throw new InvalidOperationException($"Type not found: {tid}");
                        }
                        else if (target == "instance")
                        {
                            var eid = it.Value<int?>("elementId") ?? 0;
                            if (eid <= 0) throw new InvalidOperationException("elementId required for target=instance");
                            e = doc.GetElement(new ElementId(eid));
                            if (e == null) throw new InvalidOperationException($"Element not found: {eid}");
                        }
                        else // auto
                        {
                            var eid = it.Value<int?>("elementId") ?? 0;
                            var tid = it.Value<int?>("typeId") ?? 0;
                            if (eid > 0) e = doc.GetElement(new ElementId(eid));
                            else if (tid > 0) e = doc.GetElement(new ElementId(tid)) as ElementType;
                            else throw new InvalidOperationException("elementId or typeId required (target:auto)");
                            if (e == null) throw new InvalidOperationException("Target element/type not found.");
                        }

                        var param = ParamResolver.ResolveByPayload(e, it, out var resolvedBy);
                        if (param == null) throw new InvalidOperationException("Parameter not found (name/builtIn/guid)");
                        if (param.IsReadOnly) throw new InvalidOperationException($"Parameter '{param.Definition?.Name}' is read-only");

                        var vtok = it["value"];
                        if (!UnitHelper.TrySetParameterByExternalValue(param, (vtok as JValue)?.Value, out var reason))
                            throw new InvalidOperationException(reason ?? "Failed to set value");

                        okCount++;
                        results.Add(new { ok = true, id = e.Id.IntegerValue, where = (e is ElementType ? "type" : "instance"), param = param.Definition?.Name, resolvedBy });

                        // Time-slice: if long-running, commit and restart
                        if ((DateTime.UtcNow - startAt).TotalMilliseconds > maxMillis)
                        {
                            tx.Commit();
                            tx = new Transaction(doc, "Update Parameters (batch) [cont]");
                            tx.Start();
                            TxnUtil.ConfigureProceedWithWarnings(tx);
                            startAt = DateTime.UtcNow;
                        }
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        results.Add(new { ok = false, msg = ex.Message });
                    }
                }

                tx.Commit();
                started = false;
            }
            catch (Exception ex)
            {
                try { if (started) tx.RollBack(); } catch { }
                return new { ok = false, msg = ex.Message, updatedCount = okCount, failedCount = failCount, items = results };
            }

            int next = start + slice.Count;
            bool completed = next >= total;
            if (suppressItems)
            {
                return new { ok = true, nextIndex = completed ? (int?)null : next, completed, total, updatedCount = okCount, failedCount = failCount };
            }
            else
            {
                return new { ok = true, nextIndex = completed ? (int?)null : next, completed, total, updatedCount = okCount, failedCount = failCount, items = results };
            }
        }
    }
}
