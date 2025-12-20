// ================================================================
// File: Core/Snapshot/ManifestModels.cs
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;

namespace RevitMCPAddin.Core
{
    public sealed class SnapshotManifest
    {
        public string SnapshotId { get; set; } = "";
        public string CreatedAt { get; set; } = "";
        public object Units { get; set; } = new { Length = "mm", Area = "m2", Volume = "m3" };
        public List<CategoryEntry> Categories { get; set; } = new List<CategoryEntry>();
    }

    public sealed class CategoryEntry
    {
        public string Name { get; set; } = "";
        public int Rows { get; set; }
        public string SchemaHash { get; set; } = "";
        public List<string> Columns { get; set; } = new List<string>();
        public List<Dictionary<string, object?>> Sample { get; set; } = new List<Dictionary<string, object?>>();
        public string Path { get; set; } = "";
    }

    public sealed class SnapshotResultMeta
    {
        public string SnapshotId { get; set; } = "";
        public string RootDir { get; set; } = "";
        public string ManifestPath { get; set; } = "";
        public List<CategoryFile> Files { get; set; } = new List<CategoryFile>();
    }

    public sealed class CategoryFile
    {
        public string Category { get; set; } = "";
        public string Path { get; set; } = "";
        public int RowCount { get; set; }
        public string SchemaHash { get; set; } = "";
    }
}
