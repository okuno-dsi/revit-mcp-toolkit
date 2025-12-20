#nullable enable
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ParamOps
{
    /// <summary>
    /// JSON-RPC: remove_project_parameter_binding
    /// Unbinds a project parameter by name/guid with matchMode.
    /// </summary>
    public class RemoveProjectParameterBindingCommand : IRevitCommandHandler
    {
        public string CommandName => "remove_project_parameter_binding";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("No active document.");

            var p = cmd.Params as JObject ?? new JObject();
            try
            {
                var match = p.SelectToken("match") as JObject ?? new JObject();
                var name = (match.Value<string>("name") ?? string.Empty).Trim();
                var guidStr = (match.Value<string>("guid") ?? string.Empty).Trim();
                var mode = (p.Value<string>("matchMode") ?? "name_and_guid").Trim().ToLowerInvariant();

                Guid? targetGuid = null;
                if (!string.IsNullOrWhiteSpace(guidStr) && Guid.TryParse(guidStr, out var g))
                    targetGuid = g;

                var map = doc.ParameterBindings;
                var it = map.ForwardIterator();
                it.Reset();

                var toRemove = new List<Definition>();
                var matched = new List<object>();
                while (it.MoveNext())
                {
                    var def = it.Key as Definition;
                    if (def == null) continue;
                    var defName = def.Name ?? string.Empty;
                    var ext = def as ExternalDefinition;
                    Guid? defGuid = ext?.GUID;

                    if (!IsMatch(defName, defGuid, name, targetGuid, mode))
                        continue;

                    toRemove.Add(def);
                    matched.Add(new
                    {
                        name = defName,
                        guid = defGuid?.ToString(),
                        parameterGroup = map.get_Item(def)?.ToString()
                    });
                }

                if (toRemove.Count == 0)
                {
                    return ResultUtil.Err(new
                    {
                        msg = "No matching project parameter binding found.",
                        detail = new
                        {
                            removedCount = 0,
                            matchMode = mode,
                            targetName = string.IsNullOrWhiteSpace(name) ? null : name,
                            targetGuid = targetGuid?.ToString()
                        }
                    });
                }

                int removed = 0;
                using (var tx = new Transaction(doc, "Remove Project Parameter Binding"))
                {
                    tx.Start();
                    foreach (var d in toRemove)
                    {
                        if (map.Remove(d)) removed++;
                    }
                    tx.Commit();
                }

                return ResultUtil.Ok(new
                {
                    removedCount = removed,
                    matchedDefinitions = matched
                });
            }
            catch (Exception ex)
            {
                return ResultUtil.Err($"Remove project parameter binding failed: {ex.Message}");
            }
        }

        private static bool IsMatch(string defName, Guid? defGuid, string? targetName, Guid? targetGuid, string mode)
        {
            switch (mode)
            {
                case "guid_only":
                    if (!targetGuid.HasValue || !defGuid.HasValue) return false;
                    return defGuid.Value == targetGuid.Value;

                case "name_only":
                    if (string.IsNullOrWhiteSpace(targetName)) return false;
                    return string.Equals(defName, targetName, StringComparison.OrdinalIgnoreCase);

                case "name_and_guid":
                default:
                    bool nameOk = true; bool guidOk = true;
                    if (!string.IsNullOrWhiteSpace(targetName))
                        nameOk = string.Equals(defName, targetName, StringComparison.OrdinalIgnoreCase);
                    if (targetGuid.HasValue)
                        guidOk = defGuid.HasValue && defGuid.Value == targetGuid.Value;
                    return nameOk && guidOk;
            }
        }
    }
}
