#nullable enable
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Misc
{
    public class StashSelectionCommand : IRevitCommandHandler
    {
        public string CommandName => "stash_selection";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, code = "NO_DOC", msg = "No active document." };

            var ids = uidoc.Selection.GetElementIds().Select(x => x.IntValue()).ToList();
            string docPath = string.Empty, docTitle = string.Empty; int viewId = 0;
            try { docPath = doc.PathName ?? string.Empty; } catch { }
            try { docTitle = doc.Title ?? string.Empty; } catch { }
            try { viewId = uidoc.ActiveView?.Id?.IntValue() ?? 0; } catch { }

            SelectionStash.Set(ids, docPath, docTitle, viewId);
            var snap = SelectionStash.GetSnapshot();
            return new
            {
                ok = true,
                count = ids.Count,
                elementIds = ids,
                observedAtUtc = snap.ObservedUtc,
                revision = snap.Revision,
                docPath = snap.DocPath,
                docTitle = snap.DocTitle,
                activeViewId = snap.ActiveViewId,
                hash = snap.Hash
            };
        }
    }
}

