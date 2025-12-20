#nullable enable
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ViewOps
{
    /// <summary>
    /// ensure_compare_view: Duplicate a base view with a desired name, optionally detach template.
    /// Params:
    ///   - baseViewId: int (required)
    ///   - desiredName: string (required)
    ///   - withDetailing?: bool = true
    ///   - detachTemplate?: bool = true
    ///   - onNameConflict?: 'returnExisting'|'increment'|'fail' (default: 'returnExisting')
    /// Returns: { ok:true, viewId:int, name:string, created:bool }
    /// </summary>
    public class EnsureCompareViewCommand : IRevitCommandHandler
    {
        public string CommandName => "ensure_compare_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, code = "NO_DOC", msg = "No active document." };

            var p = (JObject)(cmd.Params ?? new JObject());
            int baseViewId = p.Value<int?>("baseViewId") ?? 0;
            string desiredName = (p.Value<string>("desiredName") ?? string.Empty).Trim();
            bool withDetailing = p.Value<bool?>("withDetailing") ?? true;
            bool detachTemplate = p.Value<bool?>("detachTemplate") ?? true;
            string onNameConflict = (p.Value<string>("onNameConflict") ?? "returnExisting").Trim().ToLowerInvariant();
            if (onNameConflict != "returnexisting" && onNameConflict != "increment" && onNameConflict != "fail") onNameConflict = "returnexisting";

            if (baseViewId <= 0) return new { ok = false, code = "NO_VIEW", msg = "Missing baseViewId" };
            if (string.IsNullOrWhiteSpace(desiredName)) return new { ok = false, code = "NO_NAME", msg = "Missing desiredName" };

            var baseView = doc.GetElement(new ElementId(baseViewId)) as View;
            if (baseView == null) return new { ok = false, code = "NO_VIEW", msg = $"Base view not found: {baseViewId}" };

            // Name conflict policy
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(View)).Cast<View>()
                .FirstOrDefault(v => !v.IsTemplate && string.Equals(v.Name ?? string.Empty, desiredName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (onNameConflict == "returnexisting")
                {
                    // detach template if requested
                    if (detachTemplate && existing.ViewTemplateId != ElementId.InvalidElementId)
                    {
                        try { using (var t0 = new Transaction(doc, "Detach View Template")) { t0.Start(); existing.ViewTemplateId = ElementId.InvalidElementId; t0.Commit(); } } catch { }
                    }
                    return new { ok = true, created = false, viewId = existing.Id.IntegerValue, name = existing.Name ?? string.Empty };
                }
                if (onNameConflict == "fail")
                {
                    return new { ok = false, code = "DUPLICATE_NAME", msg = $"A view named '{desiredName}' already exists." };
                }
                // increment suffix (n)
                int n = 2; string candidate = desiredName;
                while (n < 1000)
                {
                    candidate = desiredName + " (" + n + ")";
                    var hit = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                        .FirstOrDefault(v => !v.IsTemplate && string.Equals(v.Name ?? string.Empty, candidate, StringComparison.OrdinalIgnoreCase));
                    if (hit == null) { desiredName = candidate; break; }
                    n++;
                }
            }

            // Duplicate
            try
            {
                using (var tx = new Transaction(doc, "Ensure Compare View"))
                {
                    tx.Start();
                    ElementId dupId;
                    try
                    {
                        var opt = withDetailing ? ViewDuplicateOption.WithDetailing : ViewDuplicateOption.Duplicate;
                        dupId = baseView.Duplicate(opt);
                    }
                    catch
                    {
                        dupId = baseView.Duplicate(ViewDuplicateOption.Duplicate);
                    }

                    var v = doc.GetElement(dupId) as View;
                    if (v == null) { tx.RollBack(); return new { ok = false, code = "DUP_FAIL", msg = "Duplicated view not found." }; }

                    try { v.Name = desiredName; } catch { }

                    if (detachTemplate && v.ViewTemplateId != ElementId.InvalidElementId)
                    {
                        try { v.ViewTemplateId = ElementId.InvalidElementId; } catch { }
                    }

                    tx.Commit();
                    return new { ok = true, created = true, viewId = v.Id.IntegerValue, name = v.Name ?? string.Empty };
                }
            }
            catch (Exception ex)
            {
                return new { ok = false, code = "EXCEPTION", msg = ex.Message };
            }
        }
    }
}

