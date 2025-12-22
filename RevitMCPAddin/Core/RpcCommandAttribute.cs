#nullable enable
using System;

namespace RevitMCPAddin.Core
{
    public enum RiskLevel
    {
        Low,
        Medium,
        High
    }

    /// <summary>
    /// Code-first command metadata for discovery/help/docs.
    /// This is optional for legacy commands; when absent, metadata is inferred.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class RpcCommandAttribute : Attribute
    {
        public RpcCommandAttribute(string name)
        {
            Name = name ?? string.Empty;
        }

        public string Name { get; }

        public string[] Aliases { get; set; } = Array.Empty<string>();
        public string Category { get; set; } = "Other";
        public string[] Tags { get; set; } = Array.Empty<string>();
        public RiskLevel Risk { get; set; } = RiskLevel.Low;

        public string[] Requires { get; set; } = Array.Empty<string>();
        public string[] Constraints { get; set; } = Array.Empty<string>();

        public string Summary { get; set; } = string.Empty;
        public string ExampleJsonRpc { get; set; } = string.Empty;

        // Optional overrides (kept compatible with existing Commands_Index notion)
        public string Kind { get; set; } = string.Empty;        // read|write
        public string Importance { get; set; } = string.Empty;  // low|normal|high
    }
}

