#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    /// <summary>
    /// Json.NET compatibility helpers for Revit host environments.
    /// Some hosts load an older Newtonsoft.Json which can miss newer JToken APIs.
    /// Avoid directly calling those potentially-missing methods to prevent MissingMethodException at JIT time.
    /// </summary>
    internal static class JsonNetCompat
    {
        public static string ToCompactJson(object? value)
        {
            if (value == null) return "null";
            try
            {
                var payload = value;

                // IMPORTANT:
                // Some Revit host environments can end up with mixed Json.NET types/versions.
                // Serializing JToken directly may hit MissingMethodException (WriteTo / ToString overloads).
                // Convert to plain CLR objects first.
                if (value is JToken jt)
                    payload = ToPlainObject(jt);

                return JsonConvert.SerializeObject(payload);
            }
            catch
            {
                return "null";
            }
        }

        public static string ToIndentedJson(object? value)
        {
            if (value == null) return "null";
            try
            {
                var payload = value;
                if (value is JToken jt)
                    payload = ToPlainObject(jt);

                return JsonConvert.SerializeObject(payload, Formatting.Indented);
            }
            catch
            {
                return "null";
            }
        }

        private static object? ToPlainObject(JToken token)
        {
            if (token == null) return null;

            // JValue -> raw CLR value (string/double/bool/DateTime/null)
            if (token is JValue jv) return jv.Value;

            // JObject -> Dictionary<string, object?>
            var jo = token as JObject;
            if (jo != null)
            {
                var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var prop in jo.Properties())
                {
                    var name = prop != null ? (prop.Name ?? string.Empty) : string.Empty;
                    if (string.IsNullOrEmpty(name)) continue;
                    dict[name] = prop != null ? ToPlainObject(prop.Value) : null;
                }
                return dict;
            }

            // JArray -> List<object?>
            var ja = token as JArray;
            if (ja != null)
            {
                var list = new List<object?>(ja.Count);
                foreach (var it in ja)
                    list.Add(ToPlainObject(it));
                return list;
            }

            // Fallback: treat as null (keeps things safe and avoids JToken.ToString/WriteTo paths)
            return null;
        }
    }
}
