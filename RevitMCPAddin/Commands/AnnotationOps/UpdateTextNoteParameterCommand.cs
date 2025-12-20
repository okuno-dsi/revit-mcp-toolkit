// ================================================================
// File: Commands/AnnotationOps/UpdateTextNoteParameterCommand.cs
// Purpose : Update TextNote / TextNoteType parameter with project units
// Params  : {
//   elementId:int, paramName:string, value:any, applyToType?:bool,
//   unit?: "mm|cm|m|in|ft|deg|rad"  // numeric only; overrides project display unit
// }
// Notes   : paramName is localization-friendly (Japanese/English)
// ================================================================
#nullable enable
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.AnnotationOps
{
    public class UpdateTextNoteParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "update_text_note_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = (JObject?)cmd.Params ?? new JObject();
            int elementId = p.Value<int?>("elementId") ?? 0;
            if (elementId <= 0) return new { ok = false, msg = "elementId required." };

            string paramName = (p.Value<string>("paramName") ?? "").Trim();
            if (string.IsNullOrEmpty(paramName) && p["builtInName"] == null && p["builtInId"] == null && p["guid"] == null)
                return new { ok = false, msg = "paramName or builtInName/builtInId/guid required." };

            JToken? valueTok = p["value"];
            string? unitOpt = p.Value<string>("unit");
            bool applyToType = p.Value<bool?>("applyToType") ?? false;

            var note = doc.GetElement(new ElementId(elementId)) as TextNote;
            if (note == null) return new { ok = false, msg = $"TextNote not found: {elementId}" };

            Element target = applyToType ? doc.GetElement(note.GetTypeId()) : note;
            if (target == null) return new { ok = false, msg = "Target element not found." };

            var param = ParamResolver.ResolveByPayload(target, p, out var resolvedBy);
            if (param == null) return new { ok = false, msg = $"Parameter not found (name/builtIn/guid). target={(applyToType ? "type" : "instance")}" };
            if (param.IsReadOnly) return new { ok = false, msg = $"Parameter '{paramName}' is read-only." };

            using (var t = new Transaction(doc, "update_text_note_parameter"))
            {
                t.Start();
                TxnUtil.ConfigureProceedWithWarnings(t);

                bool ok;
                string? reason = null;

                try
                {
                    ok = SetParameterWithUnits(doc, param, valueTok, unitOpt, out reason);
                }
                catch (Exception ex)
                {
                    ok = false;
                    reason = ex.Message;
                }

                if (!ok)
                {
                    t.RollBack();
                    return new { ok = false, msg = $"Failed to set '{paramName}'", reason };
                }

                t.Commit();
                return new
                {
                    ok = true,
                    elementId = note.Id.IntegerValue,
                    target = applyToType ? "type" : "instance",
                    param = param.Definition?.Name
                };
            }
        }

        private static Parameter? FindParameterByName(Element e, string paramName)
        {
            foreach (Parameter p in e.Parameters)
            {
                string n = p.Definition?.Name ?? "";
                if (string.Equals(n, paramName, StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            return null;
        }

        private static bool SetParameterWithUnits(Document doc, Parameter param, JToken? valueTok, string? unitOpt, out string? reason)
        {
            reason = null;
            if (valueTok == null || valueTok.Type == JTokenType.Null)
            {
                reason = "value is null";
                return false;
            }

            switch (param.StorageType)
            {
                case StorageType.Double:
                    {
                        // Get Spec (ForgeTypeId) if possible
                        ForgeTypeId? spec = TextNoteUnitsHelper.TryGetSpecTypeId(param.Definition);

                        if (valueTok.Type != JTokenType.Float && valueTok.Type != JTokenType.Integer)
                        {
                            reason = "numeric value required for double parameter";
                            return false;
                        }
                        double ext = valueTok.Value<double>();

                        double internalVal;
                        if (spec != null)
                            internalVal = TextNoteUnitsHelper.ToInternalByProjectUnits(doc, spec, ext, unitOpt);
                        else
                            internalVal = TextNoteUnitsHelper.ToInternalByProjectUnits(doc, SpecTypeId.Length, ext, unitOpt); // safe fallback

                        return param.Set(internalVal);
                    }

                case StorageType.Integer:
                    {
                        int iv;
                        if (valueTok.Type == JTokenType.Boolean) iv = valueTok.Value<bool>() ? 1 : 0;
                        else if (valueTok.Type == JTokenType.Integer) iv = valueTok.Value<int>();
                        else { reason = "integer or boolean required for integer parameter"; return false; }
                        return param.Set(iv);
                    }

                case StorageType.String:
                    {
                        string sv = valueTok.Type == JTokenType.String ? (valueTok.Value<string>() ?? "") : valueTok.ToString();
                        return param.Set(sv);
                    }

                case StorageType.ElementId:
                    {
                        if (valueTok.Type == JTokenType.Integer)
                        {
                            int id = valueTok.Value<int>();
                            return param.Set(new ElementId(id));
                        }
                        if (valueTok.Type == JTokenType.Null || (valueTok.Type == JTokenType.String && string.IsNullOrWhiteSpace(valueTok.Value<string>())))
                        {
                            return param.Set(ElementId.InvalidElementId);
                        }
                        reason = "elementId (int) required for elementId parameter";
                        return false;
                    }

                default:
                    reason = "unsupported storage type";
                    return false;
            }
        }
    }
}
