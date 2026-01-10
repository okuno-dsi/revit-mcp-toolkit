// ================================================================
// File: RevitMCPAddin/Core/CommandNaming.cs
// Purpose:
//   Step 4: Domain-first canonical command naming + alias mapping.
//   Canonical prefixes: sheet.*, view.*, viewport.*, element.*, doc.*, help.*
// Target : .NET Framework 4.8 / C# 8.0
// Notes  :
//   - Canonical naming is additive: legacy names remain callable.
//   - Canonical name is primarily used for discovery/help, not for handler branching.
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;

namespace RevitMCPAddin.Core
{
    public static class CommandNaming
    {
        // Explicit overrides for high-value commands and meta ops.
        private static readonly Dictionary<string, string> LegacyToCanonicalOverrides
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // MetaOps (Step 3)
                ["search_commands"] = "help.search_commands",
                ["describe_command"] = "help.describe_command",
                ["list_commands"] = "help.list_commands",
                ["ping_server"] = "help.ping_server",
                ["agent_bootstrap"] = "help.agent_bootstrap",
                ["start_command_logging"] = "help.start_command_logging",
                ["stop_command_logging"] = "help.stop_command_logging",

                // DocumentOps
                ["get_project_info"] = "doc.get_project_info",
                ["get_project_summary"] = "doc.get_project_summary",
                ["get_project_categories"] = "doc.get_project_categories",
                ["get_open_documents"] = "doc.get_open_documents",

                // Sheets (common)
                ["create_sheet"] = "sheet.create",
                ["get_sheets"] = "sheet.list",
                ["delete_sheet"] = "sheet.delete",
                ["place_view_on_sheet"] = "sheet.place_view",
                ["place_view_on_sheet_auto"] = "sheet.place_view_auto",
                ["replace_view_on_sheet"] = "sheet.replace_view",
                ["remove_view_from_sheet"] = "sheet.remove_view",
                ["get_view_placements"] = "sheet.get_view_placements",

                // Viewports (common)
                ["viewport_move_to_sheet_center"] = "viewport.move_to_sheet_center",

                // ViewOps: keep historical name but collapse to one canonical method
                ["create_clipping_3d_view_from_selection"] = "view.create_focus_3d_view_from_selection",
                ["view.create_clipping_3d_view_from_selection"] = "view.create_focus_3d_view_from_selection",

                // Batch/status canonicalization (server-side canonical name)
                ["revit_batch"] = "revit.batch",
                ["revit_status"] = "revit.status",

                // Explicit mapping for non-suffix rename cases
                ["sheet_inspect"] = "sheet.inspect",
            };

        public static bool IsCanonicalLike(string method)
        {
            if (string.IsNullOrWhiteSpace(method)) return false;
            return method.IndexOf('.') >= 0;
        }

        public static string GetCanonical(string method, Type handlerType)
        {
            var m = (method ?? string.Empty).Trim();
            if (m.Length == 0) return string.Empty;

            if (LegacyToCanonicalOverrides.TryGetValue(m, out var canon) && !string.IsNullOrWhiteSpace(canon))
                return canon.Trim();

            // Already domain-first
            if (IsCanonicalLike(m)) return m;

            var domain = InferDomain(m, handlerType);
            if (string.IsNullOrWhiteSpace(domain)) return m;
            return domain + "." + m;
        }

        public static string InferDomain(string legacyMethod, Type handlerType)
        {
            var m = (legacyMethod ?? string.Empty).Trim();
            if (m.Length == 0) return string.Empty;

            // Heuristic first: sheet/viewport hints in method string.
            var lower = m.ToLowerInvariant();
            if (lower.Contains("sheet") || lower.Contains("_on_sheet") || lower.Contains("_from_sheet"))
                return "sheet";
            if (lower.Contains("viewport"))
                return "viewport";

            // Namespace-based fallback.
            var ns = handlerType != null ? (handlerType.Namespace ?? string.Empty) : string.Empty;
            if (ns.IndexOf(".Commands.MetaOps", StringComparison.OrdinalIgnoreCase) >= 0) return "help";
            if (ns.IndexOf(".Commands.DocumentOps", StringComparison.OrdinalIgnoreCase) >= 0) return "doc";
            if (ns.IndexOf(".Commands.DocOps", StringComparison.OrdinalIgnoreCase) >= 0) return "doc";

            // View-ish (many commands affect a view even if categorized as Annotation/Visualization/Schedule/etc.)
            if (ns.IndexOf(".Commands.ViewOps", StringComparison.OrdinalIgnoreCase) >= 0) return "view";
            if (ns.IndexOf(".Commands.Visualization", StringComparison.OrdinalIgnoreCase) >= 0) return "view";
            if (ns.IndexOf(".Commands.AnnotationOps", StringComparison.OrdinalIgnoreCase) >= 0) return "view";
            if (ns.IndexOf(".Commands.Schedule", StringComparison.OrdinalIgnoreCase) >= 0) return "view";

            // Default to element.*
            return "element";
        }

        public static string Leaf(string method)
        {
            var m = (method ?? string.Empty).Trim();
            if (m.Length == 0) return string.Empty;
            var idx = m.LastIndexOf('.');
            if (idx < 0) return m;
            if (idx == m.Length - 1) return string.Empty;
            return m.Substring(idx + 1);
        }
    }
}
