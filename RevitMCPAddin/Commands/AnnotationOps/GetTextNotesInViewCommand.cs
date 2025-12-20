// ================================================================
// File: Commands/AnnotationOps/GetTextNotesInViewCommand.cs
// Purpose : List TextNotes in a view
// Target  : .NET Framework 4.8 / Revit 2023+
// Depends : Autodesk.Revit.DB, Autodesk.Revit.UI, Newtonsoft.Json.Linq
//           RevitMCPAddin.Core (IRevitCommandHandler, RequestCommand, RevitLogger)
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.AnnotationOps
{
    public class GetTextNotesInViewCommand : IRevitCommandHandler
    {
        public string CommandName => "get_text_notes_in_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = (JObject?)cmd.Params ?? new JObject();
            int? viewIdOpt = p.Value<int?>("viewId");
            View view = viewIdOpt.HasValue
                ? doc.GetElement(new ElementId(viewIdOpt.Value)) as View
                : doc.ActiveView;

            if (view == null) return new { ok = false, msg = "View not found." };

            // Shape and filters
            var shape = p["_shape"] as JObject;
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
            var page = shape?["page"] as JObject;
            int limit = Math.Max(0, page?.Value<int?>("limit") ?? int.MaxValue);
            int skip = Math.Max(0, page?.Value<int?>("skip") ?? page?.Value<int?>("offset") ?? 0);

            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;
            bool includeBBox = p.Value<bool?>("includeBBox") ?? false;
            string textContains = p.Value<string>("textContains");
            string textRegex = p.Value<string>("textRegex");
            var typeIdsArr = (p["typeIds"] as JArray)?.Values<int>()?.ToHashSet() ?? new HashSet<int>();

            var collector = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(TextNote))
                .Cast<TextNote>();

            // Early type filter (if provided)
            if (typeIdsArr.Count > 0)
            {
                collector = collector.Where(tn => typeIdsArr.Contains(tn.GetTypeId().IntegerValue));
            }

            // Apply text filters
            if (!string.IsNullOrWhiteSpace(textContains))
            {
                collector = collector.Where(tn => (tn.Text ?? string.Empty).IndexOf(textContains, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            if (!string.IsNullOrWhiteSpace(textRegex))
            {
                try
                {
                    var rx = new System.Text.RegularExpressions.Regex(textRegex, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                    collector = collector.Where(tn => rx.IsMatch(tn.Text ?? string.Empty));
                }
                catch { /* ignore bad regex */ }
            }

            // Materialize minimal set first for count
            var all = collector.Select(tn => tn).ToList();
            int totalCount = all.Count;
            if (summaryOnly)
                return new { ok = true, viewId = view.Id.IntegerValue, totalCount };

            // Paging
            IEnumerable<TextNote> paged = all;
            if (skip > 0) paged = paged.Skip(skip);
            if (limit > 0 && limit != int.MaxValue) paged = paged.Take(limit);

            if (idsOnly)
            {
                var ids = paged.Select(tn => tn.Id.IntegerValue).ToList();
                return new { ok = true, viewId = view.Id.IntegerValue, totalCount, elementIds = ids };
            }

            var items = paged.Select(tn =>
            {
                object bbox = null;
                if (includeBBox)
                {
                    try
                    {
                        var bb = tn.get_BoundingBox(view);
                        if (bb != null)
                        {
                            bbox = new
                            {
                                min = new { x = bb.Min.X, y = bb.Min.Y, z = bb.Min.Z },
                                max = new { x = bb.Max.X, y = bb.Max.Y, z = bb.Max.Z }
                            };
                        }
                    }
                    catch { }
                }
                return new
                {
                    elementId = tn.Id.IntegerValue,
                    text = tn.Text,
                    typeId = tn.GetTypeId().IntegerValue,
                    bbox
                };
            }).ToList();

            return new { ok = true, viewId = view.Id.IntegerValue, totalCount, items };
        }
    }
}
