// ================================================================
// File: Commands/AnnotationOps/CreateTextNoteCommand.cs
// Purpose : Create a TextNote at given position (project units aware)
// Params  :
//   Single: { viewId?:int, text:string, x:number, y:number, z?:number, unit?:"mm|cm|m|in|ft" , typeName?:string, refreshView?:bool }
//   Batch : { items:[{...same as Single...}], startIndex?:int, batchSize?:int, maxMillisPerTx?:int, refreshView?:bool }
// Notes   : Position is interpreted in project display units (Length) unless unit overrides.
// ================================================================
#nullable enable
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.AnnotationOps
{
    public class CreateTextNoteCommand : IRevitCommandHandler
    {
        public string CommandName => "create_text_note";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = (JObject?)cmd.Params ?? new JObject();

            // Batch path
            var itemsArr = p["items"] as JArray;
            bool refreshView = p.Value<bool?>("refreshView") ?? false;
            if (itemsArr != null && itemsArr.Count > 0)
            {
                int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
                int batchSize = Math.Max(1, p.Value<int?>("batchSize") ?? 50);
                int maxMillis = Math.Max(0, p.Value<int?>("maxMillisPerTx") ?? 100);
                var created = new System.Collections.Generic.List<object>();
                int next = startIndex;
                var sw = System.Diagnostics.Stopwatch.StartNew();

                using (var t = new Transaction(doc, "create_text_note(batch)"))
                {
                    t.Start();
                    try { RevitMCPAddin.Core.TxnUtil.ConfigureProceedWithWarnings(t); } catch { }
                    int processed = 0;
                    for (int i = startIndex; i < itemsArr.Count; i++)
                    {
                        var it = itemsArr[i] as JObject ?? new JObject();
                        var res = CreateOne(doc, it);
                        if (!res.ok)
                        {
                            // skip errored items; continue
                        }
                        else
                        {
                            created.Add(new { elementId = res.elementId, viewId = res.viewId, typeId = res.typeId });
                        }
                        processed++;
                        next = i + 1;
                        if (processed >= batchSize) break;
                        if (maxMillis > 0 && sw.ElapsedMilliseconds >= maxMillis) break;
                    }
                    t.Commit();
                }

                if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
                bool completed = (next >= itemsArr.Count);
                return new { ok = true, countCreated = created.Count, created, completed, nextIndex = completed ? (int?)null : next };
            }

            // Single path (legacy)
            var single = CreateOne(doc, p);
            if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
            if (!single.ok) return new { ok = false, msg = single.msg };
            return new { ok = true, elementId = single.elementId, viewId = single.viewId, typeId = single.typeId };
        }

        private static (bool ok, string msg, int elementId, int viewId, int typeId) CreateOne(Document doc, JObject p)
        {
            string text = (p.Value<string>("text") ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text)) return (false, "text required.", 0, 0, 0);

            int? viewIdOpt = p.Value<int?>("viewId");
            var view = viewIdOpt.HasValue ? doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewIdOpt.Value)) as View : doc.ActiveView;
            if (view == null) return (false, "View not found.", 0, 0, 0);

            string? unitOpt = p.Value<string>("unit");
            double xExt = p.Value<double?>("x") ?? 0.0;
            double yExt = p.Value<double?>("y") ?? 0.0;
            double zExt = p.Value<double?>("z") ?? 0.0;

            var pos = new XYZ(
                TextNoteUnitsHelper.ToInternalByProjectUnits(doc, SpecTypeId.Length, xExt, unitOpt),
                TextNoteUnitsHelper.ToInternalByProjectUnits(doc, SpecTypeId.Length, yExt, unitOpt),
                TextNoteUnitsHelper.ToInternalByProjectUnits(doc, SpecTypeId.Length, zExt, unitOpt)
            );

            TextNoteType tnt = null;
            string? typeName = p.Value<string>("typeName");
            if (!string.IsNullOrWhiteSpace(typeName))
            {
                tnt = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
                      .FirstOrDefault(x => x.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
                if (tnt == null) return (false, $"TextNoteType not found: '{typeName}'", 0, 0, 0);
            }
            else
            {
                tnt = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().FirstOrDefault();
                if (tnt == null) return (false, "No TextNoteType in document.", 0, 0, 0);
            }

            var opts = new TextNoteOptions(tnt.Id) { HorizontalAlignment = HorizontalTextAlignment.Left };
            TextNote tn = null;
            using (var t = new Transaction(doc, "create_text_note"))
            {
                t.Start();
                try { RevitMCPAddin.Core.TxnUtil.ConfigureProceedWithWarnings(t); } catch { }
                tn = TextNote.Create(doc, view.Id, pos, text, opts);
                t.Commit();
            }
            return (true, string.Empty, tn.Id.IntValue(), view.Id.IntValue(), tnt.Id.IntValue());
        }
    }
}


