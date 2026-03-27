using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.Core.Failures;

namespace RevitMCPAddin.Commands.Room
{
    public class SetRoomParamsBulkCommand : IRevitCommandHandler
    {
        public string CommandName => "set_room_params_bulk";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = DocumentResolver.ResolveDocument(uiapp, cmd);
            if (doc == null)
                return new { ok = false, msg = "Target document not found." };

            var p = (JObject)(cmd.Params ?? new JObject());
            var items = p["items"] as JArray;
            if (items == null || items.Count == 0)
                return new { ok = false, msg = "items[] is required.", results = Array.Empty<object>() };

            var stopOnFirstError = p.Value<bool?>("stopOnFirstError") ?? false;
            var results = new List<object>(items.Count);
            var updatedCount = 0;
            var skippedCount = 0;
            var failedCount = 0;

            using (var fhScope = new FailureHandlingScope(uiapp, FailureHandlingMode.Off))
            using (var tg = new TransactionGroup(doc, "Set Room Params (Bulk)"))
            {
                tg.Start();
                var hadWrite = false;

                foreach (var token in items)
                {
                    var item = token as JObject;
                    if (item == null)
                    {
                        failedCount++;
                        results.Add(new { ok = false, msg = "Invalid item payload." });
                        if (stopOnFirstError) break;
                        continue;
                    }

                    var elementId = item.Value<int?>("elementId") ?? 0;
                    var paramName = (item.Value<string>("paramName") ?? string.Empty).Trim();
                    if (elementId <= 0 || string.IsNullOrWhiteSpace(paramName))
                    {
                        failedCount++;
                        results.Add(new { elementId, paramName, ok = false, msg = "elementId and paramName are required." });
                        if (stopOnFirstError) break;
                        continue;
                    }

                    var room = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(elementId)) as Autodesk.Revit.DB.Architecture.Room;
                    if (room == null)
                    {
                        failedCount++;
                        results.Add(new { elementId, paramName, ok = false, msg = "Room not found." });
                        if (stopOnFirstError) break;
                        continue;
                    }

                    object valueObj = item["value"] != null ? item["value"]!.ToObject<object>() : null;
                    using (var tx = new Transaction(doc, $"Set Room Param {paramName}"))
                    {
                        tx.Start();
                        TxnUtil.ConfigureProceedWithWarnings(tx);

                        try
                        {
                            if (paramName.Equals("Name", StringComparison.OrdinalIgnoreCase) ||
                                paramName.Equals("名前", StringComparison.OrdinalIgnoreCase))
                            {
                                room.Name = valueObj?.ToString() ?? string.Empty;
                            }
                            else
                            {
                                var prm = room.LookupParameter(paramName);
                                if (prm == null)
                                {
                                    tx.RollBack();
                                    failedCount++;
                                    results.Add(new { elementId, paramName, ok = false, msg = "Parameter not found." });
                                    if (stopOnFirstError) break;
                                    continue;
                                }

                                if (prm.IsReadOnly)
                                {
                                    tx.RollBack();
                                    skippedCount++;
                                    results.Add(new { elementId, paramName, ok = false, skipped = true, msg = "Parameter is read-only." });
                                    continue;
                                }

                                if (!UnitHelper.TrySetParameterByExternalValue(prm, valueObj, out var err))
                                {
                                    tx.RollBack();
                                    failedCount++;
                                    results.Add(new { elementId, paramName, ok = false, msg = err ?? "Failed to set parameter." });
                                    if (stopOnFirstError) break;
                                    continue;
                                }
                            }

                            tx.Commit();
                            hadWrite = true;
                            updatedCount++;
                            results.Add(new { elementId, paramName, ok = true, msg = "Updated" });
                        }
                        catch (Exception ex)
                        {
                            try { tx.RollBack(); } catch { }
                            failedCount++;
                            results.Add(new { elementId, paramName, ok = false, msg = ex.Message });
                            if (stopOnFirstError) break;
                        }
                    }
                }

                if (hadWrite) tg.Assimilate();
                else tg.RollBack();
            }

            return new
            {
                ok = failedCount == 0,
                updatedCount,
                skippedCount,
                failedCount,
                results
            };
        }
    }
}
