// ================================================================
// File   : Commands/ViewOps/BuildViewsByParamValuesCommand.cs
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// Summary: Enumerate distinct parameter values for a given class in the
//          source view, then duplicate a view per value and isolate elements
//          by an equality rule using the generic isolate_by_filter_in_view.
// I/O    : Input -> {
//            sourceViewId?: int,
//            className: string,                 // e.g. "Wall"
//            param: { target:"instance"|"type"|"both", name?:string, builtInName?:string, builtInId?:int, guid?:string },
//            detachViewTemplate?: true,
//            keepAnnotations?: false,
//            withDetailing?: true,
//            naming?: { baseName?: string, separator?: string, ensureUnique?: true }
//          }
//          Output -> { ok, baseViewId, created, views:[{ viewId, name, value }] }
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
    public class BuildViewsByParamValuesCommand : IRevitCommandHandler
    {
        public string CommandName => "build_views_by_param_values";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var uidoc = uiapp?.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc == null) return new { ok = false, msg = "アクティブドキュメントが見つかりません。" };

                var p = (JObject)(cmd.Params ?? new JObject());
                int sourceViewId = p.Value<int?>("sourceViewId") ?? 0;
                string className = (p.Value<string>("className") ?? "").Trim();
                if (string.IsNullOrWhiteSpace(className)) return new { ok = false, msg = "className を指定してください。" };

                var param = p["param"] as JObject ?? new JObject();
                string target = (param.Value<string>("target") ?? "instance").Trim().ToLowerInvariant();
                string name = (param.Value<string>("name") ?? string.Empty).Trim();
                string builtInName = (param.Value<string>("builtInName") ?? string.Empty).Trim();
                int? builtInId = param.Value<int?>("builtInId");
                string guid = (param.Value<string>("guid") ?? string.Empty).Trim();

                bool detachTemplate = p.Value<bool?>("detachViewTemplate") ?? true;
                bool keepAnnotations = p.Value<bool?>("keepAnnotations") ?? false;
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

                // 1) Collect distinct values (visible in source view)
                var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var elems = new FilteredElementCollector(doc, baseView.Id)
                        .WhereElementIsNotElementType()
                        .ToElements();

                    foreach (var e in elems)
                    {
                        try
                        {
                            if (!string.Equals(e.GetType().Name ?? string.Empty, className, StringComparison.OrdinalIgnoreCase))
                                continue;

                            string v = ResolveParamAsString(doc, e, target, name, builtInName, builtInId, guid);
                            if (!string.IsNullOrWhiteSpace(v)) values.Add(v.Trim());
                        }
                        catch { }
                    }
                }
                catch { }

                if (values.Count == 0) return new { ok = true, baseViewId = baseView.Id.IntegerValue, created = 0, views = new object[0], msg = "指定パラメータの値が見つかりませんでした。" };

                // 2) Duplicate per value and isolate
                var created = new List<object>();
                foreach (var val in values.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
                {
                    View newView = null;
                    using (var t = new Transaction(doc, "Duplicate view (by param value)"))
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
                        try { newView.Name = BuildName(baseName, separator, val, suffix: " [wip]"); } catch { }
                        t.Commit();
                    }
                    if (newView == null) continue;

                    try
                    {
                        var rule = new JObject
                        {
                            ["target"] = string.IsNullOrWhiteSpace(target) ? "instance" : target,
                            ["op"] = "eq",
                            ["value"] = val
                        };
                        if (!string.IsNullOrWhiteSpace(name)) rule["name"] = name;
                        if (!string.IsNullOrWhiteSpace(builtInName)) rule["builtInName"] = builtInName;
                        if (builtInId.HasValue) rule["builtInId"] = builtInId.Value;
                        if (!string.IsNullOrWhiteSpace(guid)) rule["guid"] = guid;

                        var filter = new JObject
                        {
                            ["includeClasses"] = new JArray(className),
                            ["parameterRules"] = new JArray(rule)
                        };

                        var subParams = new JObject
                        {
                            ["viewId"] = newView.Id.IntegerValue,
                            ["detachViewTemplate"] = false,
                            ["reset"] = true,
                            ["keepAnnotations"] = keepAnnotations,
                            ["filter"] = filter
                        };
                        var subCmd = new RequestCommand { Command = "isolate_by_filter_in_view" };
                        subCmd.Params = subParams;
                        var handler = new IsolateByFilterInViewCommand();
                        handler.Execute(uiapp!, subCmd);
                    }
                    catch { }

                    try
                    {
                        using (var trn = new Transaction(doc, "Rename isolated view"))
                        {
                            trn.Start();
                            string targetName = BuildName(baseName, separator, val, suffix: null);
                            if (ensureUnique)
                            {
                                string final = EnsureUniqueViewName(doc, targetName);
                                try { newView.Name = final; } catch { }
                            }
                            else
                            {
                                try { newView.Name = targetName; } catch { }
                            }
                            trn.Commit();
                        }
                    }
                    catch { }

                    created.Add(new { viewId = newView.Id.IntegerValue, name = newView.Name ?? string.Empty, value = val });
                }

                return new { ok = true, baseViewId = baseView.Id.IntegerValue, created = created.Count, views = created };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }

        private static string ResolveParamAsString(Document doc, Element elem, string target, string name, string builtInName, int? builtInId, string guid)
        {
            try
            {
                // instance first
                string sInst = ResolveOn(elem, name, builtInName, builtInId, guid);
                if (target == "instance") return sInst;

                // type
                string sType = string.Empty;
                try { var et = doc.GetElement(elem.GetTypeId()) as Element; if (et != null) sType = ResolveOn(et, name, builtInName, builtInId, guid); } catch { }

                if (target == "type") return sType;
                // both: prefer instance value, fallback to type
                return !string.IsNullOrWhiteSpace(sInst) ? sInst : sType;
            }
            catch { return string.Empty; }
        }

        private static string ResolveOn(Element e, string name, string builtInName, int? builtInId, string guid)
        {
            try
            {
                Parameter p = null;
                if (builtInId.HasValue) { try { p = e.get_Parameter((BuiltInParameter)builtInId.Value); } catch { } }
                if (p == null && !string.IsNullOrWhiteSpace(builtInName)) { try { p = e.get_Parameter((BuiltInParameter)Enum.Parse(typeof(BuiltInParameter), builtInName, true)); } catch { } }
                if (p == null && !string.IsNullOrWhiteSpace(guid)) { try { p = e.get_Parameter(new Guid(guid)); } catch { } }
                if (p == null && !string.IsNullOrWhiteSpace(name)) { try { p = e.LookupParameter(name); } catch { } }
                if (p == null) return string.Empty;
                if (p.StorageType == StorageType.String) return p.AsString() ?? string.Empty;
                return p.AsValueString() ?? string.Empty;
            }
            catch { return string.Empty; }
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
                if (n > 200) return probe;
            }
        }
    }
}
