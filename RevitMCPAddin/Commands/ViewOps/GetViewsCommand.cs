// File: Commands/ViewOps/GetViewsCommand.cs
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
    public class GetViewsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_views";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            if (uidoc == null)
                return new { ok = false, msg = "No active document." };

            var doc = uidoc.Document;
            var p = cmd?.Params as JObject ?? new JObject();

            bool includeTemplates = p.Value<bool?>("includeTemplates") ?? false;
            bool detail = p.Value<bool?>("detail") ?? false;
            string filterType = p.Value<string>("viewType");
            string nameContains = p.Value<string>("nameContains");

            IEnumerable<View> query = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => includeTemplates || !v.IsTemplate);

            if (!string.IsNullOrWhiteSpace(filterType))
            {
                var t = filterType.Trim().ToLowerInvariant();
                query = query.Where(v => (v.ViewType.ToString() ?? string.Empty).ToLowerInvariant().Contains(t));
            }

            if (!string.IsNullOrWhiteSpace(nameContains))
            {
                var nc = nameContains.Trim().ToLowerInvariant();
                query = query.Where(v => (v.Name ?? string.Empty).ToLowerInvariant().Contains(nc));
            }

            Dictionary<int, List<int>> sheetMap = new Dictionary<int, List<int>>();
            if (detail)
            {
                try
                {
                    var vps = new FilteredElementCollector(doc)
                        .OfClass(typeof(Viewport))
                        .Cast<Viewport>();
                    foreach (var vp in vps)
                    {
                        int vid = vp.ViewId.IntegerValue;
                        int sid = vp.SheetId.IntegerValue;
                        if (vid <= 0 || sid <= 0) continue;
                        if (!sheetMap.TryGetValue(vid, out var list))
                        {
                            list = new List<int>();
                            sheetMap[vid] = list;
                        }
                        if (!list.Contains(sid)) list.Add(sid);
                    }
                }
                catch { /* best-effort */ }
            }

            var items = new List<Dictionary<string, object>>();
            foreach (var v in query)
            {
                var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["viewId"] = v.Id.IntegerValue,
                    ["uniqueId"] = v.UniqueId ?? string.Empty,
                    ["name"] = v.Name ?? string.Empty,
                    ["viewType"] = v.ViewType.ToString(),
                    ["isTemplate"] = v.IsTemplate,
                    ["canBePrinted"] = v.CanBePrinted
                };

                if (detail)
                {
                    try { d["scale"] = v.Scale; } catch { }
                    try { d["discipline"] = v.Discipline.ToString(); } catch { }

                    try
                    {
                        var vft = doc.GetElement(v.GetTypeId()) as ViewFamilyType;
                        if (vft != null)
                        {
                            d["viewFamilyTypeId"] = vft.Id.IntegerValue;
                            d["viewFamilyTypeName"] = vft.Name ?? string.Empty;
                        }
                    }
                    catch { }

                    try
                    {
                        int tid = v.ViewTemplateId?.IntegerValue ?? -1;
                        if (tid > 0)
                        {
                            d["templateViewId"] = tid;
                            if (doc.GetElement(v.ViewTemplateId) is View tv)
                                d["templateName"] = tv.Name ?? string.Empty;
                        }
                    }
                    catch { }

                    try { d["cropBoxActive"] = v.CropBoxActive; } catch { }
                    try { d["cropBoxVisible"] = v.CropBoxVisible; } catch { }

                    try
                    {
                        int vid = v.Id.IntegerValue;
                        if (sheetMap.TryGetValue(vid, out var sids) && sids != null && sids.Count > 0)
                        {
                            d["placedOnSheet"] = true;
                            d["sheetIds"] = sids.ToArray();
                        }
                        else
                        {
                            d["placedOnSheet"] = false;
                        }
                    }
                    catch { }
                }

                items.Add(d);
            }

            return new { ok = true, count = items.Count, views = items };
        }
    }
}

