#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;

namespace RevitMCPAddin.Core.ViewWorkspace
{
    internal sealed class ViewWorkspaceSnapshot
    {
        [JsonProperty("schema_version")]
        public string SchemaVersion { get; set; } = "1.0";

        [JsonProperty("saved_at_utc")]
        public string SavedAtUtc { get; set; } = "";

        [JsonProperty("doc_key")]
        public string DocKey { get; set; } = "";

        [JsonProperty("doc_title")]
        public string DocTitle { get; set; } = "";

        [JsonProperty("doc_path_hint")]
        public string DocPathHint { get; set; } = "";

        [JsonProperty("active_view_unique_id")]
        public string ActiveViewUniqueId { get; set; } = "";

        [JsonProperty("open_views")]
        public List<ViewWorkspaceViewEntry> OpenViews { get; set; } = new List<ViewWorkspaceViewEntry>();

        // Optional: ledger/authenticity metadata (best-effort, non-blocking)
        [JsonProperty("ledger")]
        public Dictionary<string, object>? Ledger { get; set; }
    }

    internal sealed class ViewWorkspaceViewEntry
    {
        [JsonProperty("view_unique_id")]
        public string ViewUniqueId { get; set; } = "";

        [JsonProperty("view_id_int")]
        public int ViewIdInt { get; set; }

        [JsonProperty("view_name")]
        public string ViewName { get; set; } = "";

        [JsonProperty("view_type")]
        public string ViewType { get; set; } = "";

        [JsonProperty("zoom")]
        public ViewWorkspaceZoom? Zoom { get; set; }

        [JsonProperty("orientation3d")]
        public ViewWorkspaceOrientation3D? Orientation3D { get; set; }
    }

    internal sealed class ViewWorkspaceZoom
    {
        [JsonProperty("corner1")]
        public ViewWorkspaceXyz Corner1 { get; set; } = new ViewWorkspaceXyz();

        [JsonProperty("corner2")]
        public ViewWorkspaceXyz Corner2 { get; set; } = new ViewWorkspaceXyz();
    }

    internal sealed class ViewWorkspaceOrientation3D
    {
        [JsonProperty("eye")]
        public ViewWorkspaceXyz Eye { get; set; } = new ViewWorkspaceXyz();

        [JsonProperty("up")]
        public ViewWorkspaceXyz Up { get; set; } = new ViewWorkspaceXyz();

        [JsonProperty("forward")]
        public ViewWorkspaceXyz Forward { get; set; } = new ViewWorkspaceXyz();

        [JsonProperty("is_perspective")]
        public bool IsPerspective { get; set; }
    }

    internal sealed class ViewWorkspaceXyz
    {
        [JsonProperty("x")]
        public double X { get; set; }

        [JsonProperty("y")]
        public double Y { get; set; }

        [JsonProperty("z")]
        public double Z { get; set; }
    }
}

