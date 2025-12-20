using System;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    /// <summary>
    /// Robust parameter resolution helper: supports builtInId, builtInName, guid, and name.
    /// Prefer builtInId → builtInName → guid → name.
    /// </summary>
    public static class ParamResolver
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, BuiltInParameter> _bipCache
            = new System.Collections.Concurrent.ConcurrentDictionary<string, BuiltInParameter>(System.StringComparer.OrdinalIgnoreCase);

        private static bool TryParseBuiltIn(string name, out BuiltInParameter bip)
        {
            bip = default;
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (_bipCache.TryGetValue(name, out bip)) return true;
            try
            {
                if (System.Enum.TryParse<BuiltInParameter>(name, true, out var parsed))
                {
                    _bipCache[name] = parsed;
                    bip = parsed;
                    return true;
                }
            }
            catch { }
            return false;
        }
        public static Parameter Resolve(Element e, string paramName, string builtInName, int? builtInId, string guid, out string resolvedBy)
        {
            resolvedBy = null;
            if (e == null) return null;
            Parameter p = null;
            // 1) BuiltInId
            if (builtInId.HasValue)
            {
                try { p = e.get_Parameter((BuiltInParameter)builtInId.Value); if (p != null) { resolvedBy = $"builtInId:{builtInId.Value}"; return p; } } catch { }
            }
            // 2) BuiltInName (enum name)
            if (!string.IsNullOrWhiteSpace(builtInName))
            {
                try
                {
                    if (TryParseBuiltIn(builtInName, out var bip))
                    {
                        p = e.get_Parameter(bip);
                    }
                    if (p != null) { resolvedBy = $"builtInName:{builtInName}"; return p; }
                }
                catch { }
            }
            // 3) GUID
            if (!string.IsNullOrWhiteSpace(guid))
            {
                try { p = e.get_Parameter(new Guid(guid)); if (p != null) { resolvedBy = $"guid:{guid}"; return p; } } catch { }
            }
            // 4) Name (localized display)
            if (!string.IsNullOrWhiteSpace(paramName))
            {
                try { p = e.LookupParameter(paramName); if (p != null) { resolvedBy = $"name:{paramName}"; return p; } } catch { }
            }
            return null;
        }

        /// <summary>
        /// Read common parameter keys from payload and resolve on the element.
        /// Keys: paramName, builtInName, builtInId, guid. Field names are configurable.
        /// </summary>
        public static Parameter ResolveByPayload(Element e, JObject payload, out string resolvedBy,
            string paramNameField = "paramName", string builtInNameField = "builtInName",
            string builtInIdField = "builtInId", string guidField = "guid")
        {
            resolvedBy = null;
            if (payload == null) return null;
            // Accept multiple alias keys for robustness
            string ReadString(params string[] keys)
            {
                foreach (var k in keys)
                {
                    try
                    {
                        var v = payload.Value<string>(k);
                        if (!string.IsNullOrWhiteSpace(v)) return v;
                    }
                    catch { }
                }
                return null;
            }

            int? ReadInt(params string[] keys)
            {
                foreach (var k in keys)
                {
                    try
                    {
                        var t = payload[k];
                        if (t == null) continue;
                        if (t.Type == Newtonsoft.Json.Linq.JTokenType.Integer) return t.Value<int>();
                        if (t.Type == Newtonsoft.Json.Linq.JTokenType.Float) return (int)t.Value<double>();
                        if (t.Type == Newtonsoft.Json.Linq.JTokenType.String)
                        {
                            if (int.TryParse(t.Value<string>(), out var iv)) return iv;
                        }
                    }
                    catch { }
                }
                return null;
            }

            int? paramId = ReadInt("paramId", "parameterId", "paramID", "parameterID");
            string paramName = ReadString(paramNameField, "name", "param_name");
            string builtInName = ReadString(builtInNameField, "builtIn", "built_in", "builtInParameter", "builtInParam", "builtinName");
            int? builtInId = ReadInt(builtInIdField, "builtIn", "built_in", "builtInId", "builtinId");
            string guid = ReadString(guidField, "paramGuid", "GUID", "guidString");

            // paramId (ElementId integer): supports both built-in (<0) and non built-in (>0) params.
            if (paramId.HasValue && e != null)
            {
                if (paramId.Value < 0)
                {
                    try
                    {
                        var p0 = e.get_Parameter((BuiltInParameter)paramId.Value);
                        if (p0 != null)
                        {
                            resolvedBy = $"paramId(builtIn):{paramId.Value}";
                            return p0;
                        }
                    }
                    catch { }
                }

                try
                {
                    foreach (Parameter pr in e.Parameters)
                    {
                        try
                        {
                            if (pr?.Id?.IntegerValue == paramId.Value)
                            {
                                resolvedBy = $"paramId:{paramId.Value}";
                                return pr;
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            return Resolve(e, paramName, builtInName, builtInId, guid, out resolvedBy);
        }
    }
}
