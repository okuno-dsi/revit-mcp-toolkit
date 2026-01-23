// File: RevitMcpServer/Docs/DocModels.cs
#nullable enable
using System.Collections.Generic;
using System.Text.Json;

namespace RevitMcpServer.Docs
{
    /// <summary>自己記述へ取り込む1メソッドのメタ情報</summary>
    public sealed class DocMethod
    {
        public string Name { get; set; } = "";
        public string Summary { get; set; } = "";
        public string[] Tags { get; set; } = new string[0];
        /// <summary>params の JSON Schema（null なら object）</summary>
        public Dictionary<string, object?>? ParamsSchema { get; set; }
        /// <summary>result の JSON Schema（null なら object）</summary>
        public Dictionary<string, object?>? ResultSchema { get; set; }
        /// <summary>由来（Server / RevitAddin / Manual 等）</summary>
        public string Source { get; set; } = "unknown";

        // --- Capability extras (optional; best-effort) ---
        public JsonElement? ParamsExample { get; set; }
        public JsonElement? ResultExample { get; set; }
        public string? RevitHandler { get; set; }
        public string? Transaction { get; set; } // Read|Write
        public string[]? SupportsFamilyKinds { get; set; }
        public string? Since { get; set; }
        public bool? Deprecated { get; set; }
    }

    /// <summary>Add-in 等から登録されるマニフェストのルート</summary>
    public sealed class DocManifest
    {
        public string Source { get; set; } = "RevitAddin";
        public List<DocMethod> Commands { get; set; } = new List<DocMethod>();
    }
}
