// ================================================================
// File   : Commands/ViewOps/BuildViewsByRulesetCommand.cs
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// Summary: Generic view builder. Duplicates a source view for each provided
//          group and applies a flexible visibility filter (same schema as
//          isolate_by_filter_in_view) to isolate desired elements.
//          - Detaches view template on new views when requested
//          - Optional annotation keeping (default: hide annotations)
//          - Deterministic, unique naming with base + separator + group name
// I/O    : Input -> {
//            sourceViewId?:int,
//            groups: [ { name:string, filter: JObject } ],
//            detachViewTemplate?: true,
//            keepAnnotations?: false,
//            withDetailing?: true,
//            naming?: { baseName?: string, separator?: string, ensureUnique?: true }
//          }
//          Output -> { ok, baseViewId, created, views:[{ viewId, name, group }] }
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ViewOps
{
    public class BuildViewsByRulesetCommand : IRevitCommandHandler
    {
        public string CommandName => "build_views_by_ruleset";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var uidoc = uiapp?.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc == null) return new { ok = false, msg = "アクティブドキュメントが見つかりません。" };

                var p = (JObject?)cmd.Params ?? new JObject();
                int sourceViewId = p.Value<int?>("sourceViewId") ?? 0;
                var groupsArr = p["groups"] as JArray;
                if (groupsArr == null || groupsArr.Count == 0)
                    return new { ok = false, msg = "groups を指定してください。" };

                bool detachTemplate = p.Value<bool?>("detachViewTemplate") ?? true;
                bool keepAnnotations = p.Value<bool?>("keepAnnotations") ?? false; // default hide annotations
                bool withDetailing = p.Value<bool?>("withDetailing") ?? true;

                var naming = p["naming"] as JObject ?? new JObject();
                string baseNameIn = (naming.Value<string>("baseName") ?? string.Empty).Trim();
                string separator = (naming.Value<string>("separator") ?? " ").Trim(); if (separator.Length == 0) separator = " ";
                bool ensureUnique = naming.Value<bool?>("ensureUnique") ?? true;

                View baseView = null;
                if (sourceViewId > 0) baseView = doc.GetElement(new ElementId(sourceViewId)) as View;
                else baseView = uidoc?.ActiveView;
                if (baseView == null) return new { ok = false, msg = "対象ビューが見つかりません。" };
                if (baseView is ViewSheet) return new { ok = false, msg = "シートビューは対象外です。" };

                string baseName = string.IsNullOrWhiteSpace(baseNameIn) ? (baseView.Name ?? "") : baseNameIn;

                var created = new List<object>();

                foreach (var gtok in groupsArr.OfType<JObject>())
                {
                    string groupName = (gtok.Value<string>("name") ?? "").Trim();
                    var filter = gtok["filter"] as JObject ?? new JObject();
                    if (string.IsNullOrWhiteSpace(groupName)) groupName = "Group" + (created.Count + 1).ToString();

                    // 1) Duplicate
                    View newView = null;
                    using (var t = new Transaction(doc, "Duplicate view (ruleset)"))
                    {
                        t.Start();
                        ElementId dupId;
                        try { dupId = baseView.Duplicate(withDetailing ? ViewDuplicateOption.WithDetailing : ViewDuplicateOption.Duplicate); }
                        catch { dupId = baseView.Duplicate(ViewDuplicateOption.Duplicate); }
                        newView = doc.GetElement(dupId) as View;
                        if (newView == null) { try { t.RollBack(); } catch { } continue; }
                        if (detachTemplate)
                        {
                            try { newView.ViewTemplateId = ElementId.InvalidElementId; } catch { }
                        }
                        // provisional name to avoid conflicts during ops
                        try { newView.Name = BuildName(baseName, separator, groupName, suffix: " [wip]"); } catch { }
                        t.Commit();
                    }
                    if (newView == null) continue;

                    // 2) Apply filter using existing generic handler (reuse logic/consistency)
                    try
                    {
                        var subParams = new JObject
                        {
                            ["viewId"] = newView.Id.IntegerValue,
                            ["detachViewTemplate"] = false, // already detached
                            ["reset"] = true,
                            ["keepAnnotations"] = keepAnnotations,
                            ["filter"] = filter
                        };
                        var subCmd = new RequestCommand { Command = "isolate_by_filter_in_view" };
                        subCmd.Params = subParams;
                        var handler = new IsolateByFilterInViewCommand();
                        handler.Execute(uiapp!, subCmd);
                    }
                    catch { /* best-effort */ }

                    // 3) Rename to final name (ensure unique when requested)
                    try
                    {
                        using (var trn = new Transaction(doc, "Rename ruleset view"))
                        {
                            trn.Start();
                            string target = BuildName(baseName, separator, groupName, suffix: null);
                            if (ensureUnique)
                            {
                                string final = EnsureUniqueViewName(doc, target);
                                try { newView.Name = final; } catch { /* ignore */ }
                            }
                            else
                            {
                                try { newView.Name = target; } catch { /* ignore */ }
                            }
                            trn.Commit();
                        }
                    }
                    catch { }

                    created.Add(new
                    {
                        viewId = newView.Id.IntegerValue,
                        name = newView.Name ?? string.Empty,
                        group = groupName
                    });
                }

                return new
                {
                    ok = true,
                    baseViewId = baseView.Id.IntegerValue,
                    created = created.Count,
                    views = created
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }

        private static string BuildName(string baseName, string sep, string groupName, string? suffix)
        {
            string nm = (baseName ?? string.Empty).Trim();
            string g = (groupName ?? string.Empty).Trim();
            string sfx = suffix ?? string.Empty;
            nm = nm.Replace('\r', ' ').Replace('\n', ' ');
            g = g.Replace('\r', ' ').Replace('\n', ' ');
            return string.IsNullOrWhiteSpace(g) ? (nm + sfx) : (nm + sep + g + sfx);
        }

        private static string EnsureUniqueViewName(Document doc, string desired)
        {
            string probe = desired;
            int n = 1;
            while (true)
            {
                try
                {
                    bool exists = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .Any(v => string.Equals(v.Name ?? string.Empty, probe, StringComparison.OrdinalIgnoreCase));
                    if (!exists) return probe;
                }
                catch { }
                n++;
                probe = desired + " (" + n.ToString() + ")";
                if (n > 200) return probe; // give up after many tries
            }
        }
    }
}

