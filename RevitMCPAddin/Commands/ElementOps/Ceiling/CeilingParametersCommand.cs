// File: RevitMCPAddin/Commands/ElementOps/Ceiling/CeilingParametersCommand.cs  (UnitHelper化)
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Ceiling
{
    /// <summary>Retrieve all instance parameters for a specific ceiling element (UnitHelper normalized).</summary>
    public class GetCeilingInstanceParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_ceiling_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            var elemId = new ElementId(p.Value<int>("elementId"));
            var ceil = doc.GetElement(elemId) as Autodesk.Revit.DB.Ceiling;
            if (ceil == null) return ResultUtil.Err("Ceiling not found.", "NOT_FOUND");

            var list = ceil.Parameters.Cast<Parameter>()
                .Select(prm => UnitHelper.ParamToSiInfo(prm)) // {name, storage, isReadOnly, dataType, value(SI), display}
                .ToList();

            return ResultUtil.Ok(new { parameters = list });
        }
    }

    /// <summary>Set an instance parameter on a specific ceiling element (UnitHelper aware).</summary>
    public class SetCeilingInstanceParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "set_ceiling_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            var elemId = new ElementId(p.Value<int>("elementId"));
            var ceil = doc.GetElement(elemId) as Autodesk.Revit.DB.Ceiling;
            if (ceil == null) return ResultUtil.Err("Ceiling not found.", "NOT_FOUND");

            var prm = ParamResolver.ResolveByPayload(ceil, p, out var resolvedBy);
            if (prm == null) return ResultUtil.Err($"Parameter not found (name/builtIn/guid)", "PARAM_NOT_FOUND");
            if (prm.IsReadOnly) return ResultUtil.Err($"パラメータ '{prm.Definition?.Name}' は読み取り専用です", "READ_ONLY");

            var token = p["value"];
            using (var tx = new Transaction(doc, "Set Ceiling Instance Parameter"))
            {
                tx.Start();
                var ok = UnitHelper.TrySetParameterFromSi(prm, token, out var reason);
                if (!ok)
                {
                    tx.RollBack();
                    return ResultUtil.Err(reason ?? "Unsupported or invalid value.", "SET_FAILED");
                }
                tx.Commit();
            }
            return ResultUtil.Ok();
        }
    }

    /// <summary>Retrieve all type parameters for a specific ceiling type (UnitHelper normalized).</summary>
    public class GetCeilingTypeParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_ceiling_type_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            var typeId = new ElementId(p.Value<int>("typeId"));
            var ct = doc.GetElement(typeId) as CeilingType;
            if (ct == null) return ResultUtil.Err("CeilingType not found.", "NOT_FOUND");

            var list = ct.Parameters.Cast<Parameter>()
                .Select(prm => UnitHelper.ParamToSiInfo(prm))
                .ToList();

            return ResultUtil.Ok(new { parameters = list });
        }
    }

    /// <summary>Set a type parameter on a specific ceiling type (UnitHelper aware).</summary>
    public class SetCeilingTypeParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "set_ceiling_type_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            var typeId = new ElementId(p.Value<int>("typeId"));
            var ct = doc.GetElement(typeId) as CeilingType;
            if (ct == null) return ResultUtil.Err("CeilingType not found.", "NOT_FOUND");

            var prm = ParamResolver.ResolveByPayload(ct, p, out var resolvedBy2);
            if (prm == null) return ResultUtil.Err($"Parameter not found (name/builtIn/guid)", "PARAM_NOT_FOUND");
            if (prm.IsReadOnly) return ResultUtil.Err($"パラメータ '{prm.Definition?.Name}' は読み取り専用です", "READ_ONLY");

            var token = p["value"];
            using (var tx = new Transaction(doc, "Set Ceiling Type Parameter"))
            {
                tx.Start();
                var ok = UnitHelper.TrySetParameterFromSi(prm, token, out var reason);
                if (!ok)
                {
                    tx.RollBack();
                    return ResultUtil.Err(reason ?? "Unsupported or invalid value.", "SET_FAILED");
                }
                tx.Commit();
            }
            return ResultUtil.Ok();
        }
    }
}
