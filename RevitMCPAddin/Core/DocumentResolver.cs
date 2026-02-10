// ================================================================
// File: Core/DocumentResolver.cs
// Purpose: Resolve target Document for read commands, including
//          non-active documents, based on docGuid / title / path.
// Notes  : Uses the same surrogate GUID convention as
//          Commands/Rpc/GetOpenDocumentsCommand.cs (prefer MCP Ledger docKey)
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8.0
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core.Ledger;

namespace RevitMCPAddin.Core
{
    public static class DocumentResolver
    {
        /// <summary>
        /// Resolve a target Document for a command.
        /// Priority:
        ///   1) meta/params.docGuid (matches GetOpenDocumentsCommand guid)
        ///   2) meta/params.docPath or documentPath
        ///   3) meta/params.docTitle or documentTitle
        ///   4) Fallback: ActiveUIDocument.Document
        /// </summary>
        public static Document? ResolveDocument(UIApplication? uiapp, RequestCommand? cmd)
        {
            var active = uiapp?.ActiveUIDocument?.Document;
            var app = uiapp?.Application;
            if (app == null)
            {
                return active;
            }

            string? docGuid = null;
            string? docTitle = null;
            string? docPath = null;

            // --- params ---
            try
            {
                var p = cmd?.Params as JObject;
                if (p != null)
                {
                    docGuid = p.Value<string>("docGuid");
                    docTitle = p.Value<string>("docTitle") ?? p.Value<string>("documentTitle");
                    docPath = p.Value<string>("docPath") ?? p.Value<string>("documentPath");
                }
            }
            catch
            {
                // best-effort; ignore
            }

            // --- meta.Extensions ---
            try
            {
                var meta = cmd?.Meta;
                var ext = meta?.Extensions;
                if (ext != null)
                {
                    if (string.IsNullOrWhiteSpace(docGuid) &&
                        ext.TryGetValue("docGuid", out var gTok) &&
                        gTok != null &&
                        gTok.Type == JTokenType.String)
                    {
                        docGuid = gTok.ToObject<string>();
                    }

                    if (string.IsNullOrWhiteSpace(docTitle) &&
                        ext.TryGetValue("docTitle", out var tTok) &&
                        tTok != null &&
                        tTok.Type == JTokenType.String)
                    {
                        docTitle = tTok.ToObject<string>();
                    }

                    if (string.IsNullOrWhiteSpace(docTitle) &&
                        ext.TryGetValue("documentTitle", out var dtTok) &&
                        dtTok != null &&
                        dtTok.Type == JTokenType.String)
                    {
                        docTitle = dtTok.ToObject<string>();
                    }

                    if (string.IsNullOrWhiteSpace(docPath) &&
                        ext.TryGetValue("docPath", out var pTok) &&
                        pTok != null &&
                        pTok.Type == JTokenType.String)
                    {
                        docPath = pTok.ToObject<string>();
                    }

                    if (string.IsNullOrWhiteSpace(docPath) &&
                        ext.TryGetValue("documentPath", out var dpTok) &&
                        dpTok != null &&
                        dpTok.Type == JTokenType.String)
                    {
                        docPath = dpTok.ToObject<string>();
                    }
                }
            }
            catch
            {
                // ignore
            }

            bool hasHint =
                !string.IsNullOrWhiteSpace(docGuid) ||
                !string.IsNullOrWhiteSpace(docTitle) ||
                !string.IsNullOrWhiteSpace(docPath);

            if (!hasHint)
            {
                // No explicit target requested; use active document.
                return active;
            }

            var docs = new List<Document>();
            foreach (Document d in app.Documents)
            {
                docs.Add(d);
            }

            // --- 1) GUID match (GetOpenDocuments surrogate GUID convention) ---
            if (!string.IsNullOrWhiteSpace(docGuid))
            {
                foreach (var d in docs)
                {
                    var g = GetSurrogateGuid(d);
                    if (!string.IsNullOrEmpty(g) &&
                        string.Equals(g, docGuid, StringComparison.OrdinalIgnoreCase))
                    {
                        return d;
                    }
                }
            }

            // --- 2) Path match ---
            if (!string.IsNullOrWhiteSpace(docPath))
            {
                foreach (var d in docs)
                {
                    string? pth = null;
                    try { pth = d.PathName; }
                    catch { /* ignore */ }

                    if (!string.IsNullOrEmpty(pth) &&
                        string.Equals(pth, docPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return d;
                    }
                }
            }

            // --- 3) Title match ---
            if (!string.IsNullOrWhiteSpace(docTitle))
            {
                var titleMatches = new List<Document>();
                foreach (var d in docs)
                {
                    string? t = null;
                    try { t = d.Title; }
                    catch { /* ignore */ }

                    if (!string.IsNullOrEmpty(t) &&
                        string.Equals(t, docTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        titleMatches.Add(d);
                    }
                }

                if (titleMatches.Count == 1)
                    return titleMatches[0];

                if (titleMatches.Count > 1)
                {
                    // Prefer active document if included in matches.
                    if (active != null && titleMatches.Contains(active))
                        return active;

                    return titleMatches[0];
                }
            }

            // Fallback: active document (may be null).
            return active;
        }

        /// <summary>
        /// Compute a surrogate GUID compatible with GetOpenDocumentsCommand.
        /// </summary>
        private static string GetSurrogateGuid(Document? d)
        {
            if (d == null) return string.Empty;

            string? guid = null;
            try
            {
                if (!LedgerDocKeyProvider.TryGetDocKey(d, out guid, out _, out _))
                    LedgerDocKeyProvider.TryGetOrCreateDocKey(d, createIfMissing: true, out guid, out _, out _);
            }
            catch { /* ignore */ }

            if (string.IsNullOrWhiteSpace(guid))
            {
                try
                {
                    var pi = d.ProjectInformation;
                    if (pi != null)
                    {
                        guid = pi.UniqueId;
                    }
                }
                catch
                {
                    // ignore
                }
            }

            if (string.IsNullOrWhiteSpace(guid))
            {
                string? title = null;
                string? path = null;

                try { title = d.Title; }
                catch { /* ignore */ }

                try { path = d.PathName; }
                catch { /* ignore */ }

                if (string.IsNullOrEmpty(title)) title = string.Empty;
                if (string.IsNullOrEmpty(path)) path = string.Empty;

                try
                {
                    var sig = string.Format("{0}|{1}", title, path);
                    guid = Convert.ToBase64String(Encoding.UTF8.GetBytes(sig));
                }
                catch
                {
                    guid = string.Empty;
                }
            }

            return guid ?? string.Empty;
        }
    }
}
