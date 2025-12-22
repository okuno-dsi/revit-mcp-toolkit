#nullable enable
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Misc
{
    public class RestoreSelectionCommand : IRevitCommandHandler
    {
        public string CommandName => "restore_selection";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, code = "NO_DOC", msg = "No active document." };

            var ids = SelectionStash.GetIds();
            var elemIds = new HashSet<ElementId>(ids.Select(i => Autodesk.Revit.DB.ElementIdCompat.From(i)));

            try
            {
                uidoc.Selection.SetElementIds(elemIds);
                return new { ok = true, count = elemIds.Count };
            }
            catch (System.Exception ex)
            {
                return new { ok = false, code = "SET_FAIL", msg = ex.Message };
            }
        }
    }
}

