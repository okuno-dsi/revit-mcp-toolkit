// File: RevitMcpServer/Docs/SchemaUtils.cs
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using RevitMCP.Abstractions.Docs;

namespace RevitMcpServer.Docs
{
    /// <summary>.NET Type  JSON Schema (ŏ) ϊ[eBeB</summary>
    public static class SchemaUtils
    {
        public static Dictionary<string, object?> ToJsonSchema(Type t, HashSet<Type>? seen = null)
        {
            seen ??= new HashSet<Type>();
            if (seen.Contains(t)) return new() { ["type"] = "object" }; // cycle guard
            seen.Add(t);

            // Nullable<T>
            if (Nullable.GetUnderlyingType(t) is Type ut)
                return ToJsonSchema(ut, seen);

            // Primitives
            if (t == typeof(string)) return new() { ["type"] = "string" };
            if (t == typeof(bool)) return new() { ["type"] = "boolean" };
            if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)) return new() { ["type"] = "integer" };
            if (t == typeof(float) || t == typeof(double) || t == typeof(decimal)) return new() { ["type"] = "number" };
            if (t.IsEnum) return new() { ["type"] = "string", ["enum"] = Enum.GetNames(t) };

            // IDictionary<string, T>
            if (typeof(IDictionary).IsAssignableFrom(t) || ImplementsGeneric(t, typeof(IDictionary<,>)))
            {
                var valueType = t.IsGenericType ? t.GetGenericArguments().Last() : typeof(object);
                return new()
                {
                    ["type"] = "object",
                    ["additionalProperties"] = ToJsonSchema(valueType, seen)
                };
            }

            // IEnumerable<T>
            if (t != typeof(string) && (typeof(IEnumerable).IsAssignableFrom(t) || ImplementsGeneric(t, typeof(IEnumerable<>))))
            {
                var itemType = t.IsArray ? t.GetElementType()! : (t.IsGenericType ? t.GetGenericArguments().First() : typeof(object));
                return new()
                {
                    ["type"] = "array",
                    ["items"] = ToJsonSchema(itemType, seen)
                };
            }

            // Complex object -> properties
            var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p.GetMethod != null && p.GetMethod.IsPublic)
                         .ToArray();
            var required = new List<string>();
            var dict = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>()
            };
            var propDict = (Dictionary<string, object?>)dict["properties"]!;

            foreach (var p in props)
            {
                var name = p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? p.Name;
                var fieldAttr = p.GetCustomAttribute<RpcFieldAttribute>();
                var sch = ToJsonSchema(p.PropertyType, seen);
                if (fieldAttr?.Description != null) sch["description"] = fieldAttr.Description;
                if (fieldAttr?.Example != null) sch["example"] = fieldAttr.Example;
                propDict[name] = sch;
                if (fieldAttr?.Required == true) required.Add(name);
            }
            if (required.Count > 0) dict["required"] = required;
            return dict;
        }

        private static bool ImplementsGeneric(Type t, Type generic)
        {
            return t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == generic)
                || (t.IsGenericType && t.GetGenericTypeDefinition() == generic);
        }
    }
}
