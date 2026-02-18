// File: RevitMCP.Abstractions/Docs/RpcDocAttributes.cs
#nullable enable
using System;

namespace RevitMCP.Abstractions.Docs
{
    /// <summary>R}h̊TvE^Ot</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class RpcDocAttribute : Attribute
    {
        public string Summary { get; }
        public string[] Tags { get; }
        public RpcDocAttribute(string summary, params string[] tags)
        {
            Summary = summary;
            Tags = tags ?? Array.Empty<string>();
        }
    }

    /// <summary>DTO vpeBɐEK{Et^</summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class RpcFieldAttribute : Attribute
    {
        public string? Description { get; }
        public bool Required { get; }
        public object? Example { get; }
        public RpcFieldAttribute(string? description = null, bool required = false, object? example = null)
        {
            Description = description;
            Required = required;
            Example = example;
        }
    }
}
