// ================================================================
// File: Core/Compat/RequestShim.cs
// Purpose: RequestCommand 仕様差分を吸収（params / command / method など）
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    internal static class ReqShim
    {
        // Params候補プロパティ名
        private static readonly string[] ParamNames = new[]
        { "params", "Params", "data", "Data", "arguments", "Arguments", "payload", "Payload" };

        // Method/Command候補プロパティ名
        private static readonly string[] MethodNames = new[]
        { "command", "Command", "method", "Method", "name", "Name" };

        public static JObject Params(object req)
        {
            if (req == null) return new JObject();
            var t = req.GetType();
            foreach (var n in ParamNames)
            {
                var pi = t.GetProperty(n, BindingFlags.Public | BindingFlags.Instance);
                if (pi == null) continue;
                var val = pi.GetValue(req);
                if (val is JObject jo) return jo;
                if (val is string s)
                {
                    try { return JObject.Parse(s); } catch { }
                }
            }
            return new JObject();
        }

        public static string Method(object req)
        {
            if (req == null) return string.Empty;
            var t = req.GetType();
            foreach (var n in MethodNames)
            {
                var pi = t.GetProperty(n, BindingFlags.Public | BindingFlags.Instance);
                if (pi == null) continue;
                var val = pi.GetValue(req)?.ToString();
                if (!string.IsNullOrWhiteSpace(val)) return val!;
            }
            return string.Empty;
        }

        // 安全な JToken→型変換（存在しなければデフォルト）
        public static T Get<T>(JObject jo, string propName, T defaultValue = default!)
        {
            if (jo == null) return defaultValue;
            if (!jo.TryGetValue(propName, out var token) || token == null || token.Type == JTokenType.Null)
                return defaultValue;
            try { return token.ToObject<T>()!; } catch { return defaultValue; }
        }

        // ネスト: Get<int?>("grid.nx", null) 形式もOK
        public static T GetPath<T>(JObject jo, string path, T defaultValue = default!)
        {
            if (jo == null) return defaultValue;
            var token = jo.SelectToken(path);
            if (token == null || token.Type == JTokenType.Null) return defaultValue;
            try { return token.ToObject<T>()!; } catch { return defaultValue; }
        }
    }
}
