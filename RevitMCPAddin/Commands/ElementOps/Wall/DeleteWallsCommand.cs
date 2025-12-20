using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    public class DeleteWallsCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_walls";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = (JObject)(cmd.Params ?? new JObject());
            var arr = p["elementIds"] as JArray;
            if (arr == null || arr.Count == 0)
                return new { ok = false, msg = "Provide elementIds: int[]" };

            var ids = new List<ElementId>();
            foreach (var t in arr)
            {
                try { ids.Add(new ElementId(t.Value<int>())); } catch { }
            }
            if (ids.Count == 0) return new { ok = true, deletedCount = 0, deletedElementIds = new int[0] };

            ICollection<ElementId> deleted = null;
            using (var tx = new Transaction(doc, "Delete Walls (bulk)"))
            {
                tx.Start();
                try { deleted = doc.Delete(ids); tx.Commit(); }
                catch (Exception ex) { tx.RollBack(); return new { ok = false, msg = ex.Message }; }
            }

            var ret = deleted?.Select(e => e.IntegerValue).ToArray() ?? Array.Empty<int>();
            return new { ok = true, deletedCount = ret.Length, deletedElementIds = ret };
        }
    }
}
