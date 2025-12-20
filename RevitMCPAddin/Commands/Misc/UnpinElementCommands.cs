// File: Commands/Misc/UnpinElementCommands.cs
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Misc
{
    // 1) 単一要素のピン留め解除
    public class UnpinElementCommand : IRevitCommandHandler
    {
        public string CommandName => "unpin_element";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = cmd.Params as JObject ?? new JObject();

            Element target = null;
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) target = doc.GetElement(new ElementId(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) target = doc.GetElement(uid);

            if (target == null) return new { ok = false, msg = "elementId または uniqueId で要素が見つかりません。" };

            if (!target.Pinned)
            {
                return new { ok = true, elementId = target.Id.IntegerValue, uniqueId = target.UniqueId, changed = false, wasPinned = false };
            }

            using (var tx = new Transaction(doc, "Unpin Element"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                try
                {
                    target.Pinned = false;
                    tx.Commit();
                    return new { ok = true, elementId = target.Id.IntegerValue, uniqueId = target.UniqueId, changed = true, wasPinned = true };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = ex.Message };
                }
            }
        }
    }

    // 2) 複数要素のピン留め解除
    public class UnpinElementsCommand : IRevitCommandHandler
    {
        public string CommandName => "unpin_elements";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = cmd.Params as JObject ?? new JObject();
            var idArr = p["elementIds"] as JArray;
            if (idArr == null || idArr.Count == 0)
                return new { ok = false, msg = "elementIds 配列が必要です。" };

            var ids = new List<ElementId>();
            foreach (var t in idArr)
            {
                if (t.Type == JTokenType.Integer)
                    ids.Add(new ElementId(t.Value<int>()));
            }
            if (ids.Count == 0)
                return new { ok = false, msg = "有効な elementIds がありません。" };

            int processed = 0;
            int changed = 0;
            var failed = new List<int>();

            using (var tx = new Transaction(doc, "Unpin Elements"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                try
                {
                    foreach (var id in ids)
                    {
                        var e = doc.GetElement(id);
                        if (e == null) { failed.Add(id.IntegerValue); continue; }
                        processed++;
                        if (e.Pinned)
                        {
                            try
                            {
                                e.Pinned = false;
                                changed++;
                            }
                            catch
                            {
                                failed.Add(id.IntegerValue);
                            }
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = ex.Message };
                }
            }

            return new
            {
                ok = true,
                requested = ids.Count,
                processed,
                changed,
                failedIds = failed
            };
        }
    }
}

