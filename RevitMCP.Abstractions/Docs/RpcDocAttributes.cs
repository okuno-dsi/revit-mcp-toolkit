// File: RevitMCP.Abstractions/Docs/RpcDocAttributes.cs
#nullable enable
using System;

namespace RevitMCP.Abstractions.Docs
{
    /// <summary>コマンドの概要・タグを付ける</summary>
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

    /// <summary>DTO プロパティに説明・必須・例を付与</summary>
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
