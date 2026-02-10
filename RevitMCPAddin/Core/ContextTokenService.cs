#nullable enable
// ================================================================
// File   : Core/ContextTokenService.cs
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// Purpose:
//   Step 7: Context tokens to detect state drift between human/UI edits and agent assumptions.
//   Token = hash(docGuid + docPath + activeViewId + contextRevision + selectionIds)
// Notes:
//   - Revision is tracked per document (by MCP Ledger DocKey; fallback to ProjectInformation.UniqueId).
//   - Token is derived from the *current* UI context (active doc/view + selection).
// ================================================================
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitMCPAddin.Core
{
    internal sealed class ContextSnapshot
    {
        public string tokenVersion { get; set; } = ContextTokenService.TokenVersion;
        public long revision { get; set; }
        public string contextToken { get; set; } = string.Empty;

        public string docGuid { get; set; } = string.Empty;
        public string docTitle { get; set; } = string.Empty;
        public string docPath { get; set; } = string.Empty;

        public int activeViewId { get; set; }
        public string activeViewName { get; set; } = string.Empty;
        public string activeViewType { get; set; } = string.Empty;
        public string rawActiveViewType { get; set; } = string.Empty;

        public int selectionCount { get; set; }
        public bool selectionIdsTruncated { get; set; }
        public int[] selectionIds { get; set; } = Array.Empty<int>();
        public string selectionSource { get; set; } = "live"; // live|live-retry|stash
        public DateTime selectionObservedUtc { get; set; } = DateTime.MinValue;
        public int selectionStashAgeMs { get; set; }
    }

    internal static class ContextTokenService
    {
        public const string TokenVersion = "ctx.v1";

        private static readonly ConcurrentDictionary<string, long> _revisionByDocGuid
            = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        public static long GetRevision(Document doc)
        {
            var key = GetDocGuid(doc);
            if (string.IsNullOrWhiteSpace(key)) key = GetDocPath(doc);
            if (string.IsNullOrWhiteSpace(key)) return 0;
            return _revisionByDocGuid.TryGetValue(key, out var v) ? v : 0;
        }

        public static long BumpRevision(Document doc, string reason)
        {
            var key = GetDocGuid(doc);
            if (string.IsNullOrWhiteSpace(key)) key = GetDocPath(doc);
            if (string.IsNullOrWhiteSpace(key)) return 0;

            return _revisionByDocGuid.AddOrUpdate(key, 1, (_, old) => old + 1);
        }

        public static string GetContextToken(UIApplication uiapp)
        {
            var snap = Capture(uiapp, includeSelectionIds: false, maxSelectionIds: 0);
            return snap.contextToken ?? string.Empty;
        }

        public static ContextSnapshot Capture(
            UIApplication uiapp,
            bool includeSelectionIds,
            int maxSelectionIds,
            int selectionRetryMaxWaitMs = 0,
            int selectionRetryPollMs = 150,
            bool selectionFallbackToStash = true,
            int selectionStashMaxAgeMs = 2000)
        {
            var snap = new ContextSnapshot();

            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null)
            {
                snap.contextToken = "ctx-nodoc";
                snap.revision = 0;
                return snap;
            }

            snap.docGuid = GetDocGuid(doc);
            snap.docTitle = SafeGet(() => doc.Title) ?? string.Empty;
            snap.docPath = SafeGet(() => doc.PathName) ?? string.Empty;

            var av = SafeGet(() => uidoc.ActiveView);
            if (av != null)
            {
                snap.activeViewId = av.Id.IntValue();
                snap.activeViewName = av.Name ?? string.Empty;
                snap.activeViewType = av.ViewType.ToString();
                snap.rawActiveViewType = av.GetType().Name;
            }

            string selSource = "live";
            DateTime selObservedUtc = DateTime.UtcNow;
            int selStashAgeMs = 0;

            var selIds = GetSelectionIdsSorted(uidoc);
            if (selIds.Length == 0 && selectionRetryMaxWaitMs > 0)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int pollMs = Math.Min(1000, Math.Max(50, selectionRetryPollMs));
                while (sw.ElapsedMilliseconds < selectionRetryMaxWaitMs)
                {
                    try { System.Threading.Thread.Sleep(pollMs); } catch { break; }
                    selIds = GetSelectionIdsSorted(uidoc);
                    if (selIds.Length > 0)
                    {
                        selSource = "live-retry";
                        selObservedUtc = DateTime.UtcNow;
                        break;
                    }
                }
            }

            if (selIds.Length == 0 && selectionFallbackToStash)
            {
                try
                {
                    var stash = SelectionStash.GetLastNonEmptySnapshot();
                    var ageMs = (int)Math.Max(0, (DateTime.UtcNow - stash.ObservedUtc).TotalMilliseconds);
                    bool docMatch = string.IsNullOrWhiteSpace(stash.DocPath)
                                    || string.Equals(stash.DocPath, snap.docPath ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(stash.DocTitle, snap.docTitle ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                    bool viewMatch = (stash.ActiveViewId == 0) || (stash.ActiveViewId == snap.activeViewId);
                    if (stash.Ids != null && stash.Ids.Length > 0
                        && (selectionStashMaxAgeMs <= 0 || ageMs <= selectionStashMaxAgeMs)
                        && docMatch && viewMatch)
                    {
                        selIds = stash.Ids;
                        selSource = "stash";
                        selObservedUtc = stash.ObservedUtc;
                        selStashAgeMs = ageMs;
                    }
                }
                catch { /* ignore */ }
            }
            snap.selectionCount = selIds.Length;
            snap.selectionSource = selSource;
            snap.selectionObservedUtc = selObservedUtc;
            snap.selectionStashAgeMs = selStashAgeMs;

            snap.revision = GetRevision(doc);

            // Token uses the full selection list (not truncated).
            snap.contextToken = ComputeToken(
                docGuid: snap.docGuid,
                docPath: snap.docPath,
                activeViewId: snap.activeViewId,
                revision: snap.revision,
                selectionIdsSorted: selIds);

            if (includeSelectionIds)
            {
                if (maxSelectionIds > 0 && selIds.Length > maxSelectionIds)
                {
                    snap.selectionIdsTruncated = true;
                    var head = new int[maxSelectionIds];
                    Array.Copy(selIds, head, maxSelectionIds);
                    snap.selectionIds = head;
                }
                else
                {
                    snap.selectionIds = selIds;
                }
            }
            else
            {
                snap.selectionIds = Array.Empty<int>();
            }

            return snap;
        }

        private static int[] GetSelectionIdsSorted(UIDocument uidoc)
        {
            try
            {
                var sel = uidoc?.Selection?.GetElementIds();
                if (sel == null || sel.Count == 0) return Array.Empty<int>();

                var list = new List<int>(sel.Count);
                foreach (var id in sel)
                {
                    if (id == null) continue;
                    var v = id.IntValue();
                    if (v != 0) list.Add(v);
                }
                list.Sort();
                return list.ToArray();
            }
            catch
            {
                return Array.Empty<int>();
            }
        }

        private static string ComputeToken(string docGuid, string docPath, int activeViewId, long revision, int[] selectionIdsSorted)
        {
            try
            {
                var normPath = NormalizeForHash(docPath ?? string.Empty);
                var normGuid = NormalizeForHash(docGuid ?? string.Empty);

                var sb = new StringBuilder(256);
                sb.Append(TokenVersion).Append('|');
                sb.Append(normGuid).Append('|');
                sb.Append(normPath).Append('|');
                sb.Append(activeViewId.ToString(CultureInfo.InvariantCulture)).Append('|');
                sb.Append(revision.ToString(CultureInfo.InvariantCulture)).Append('|');

                if (selectionIdsSorted != null && selectionIdsSorted.Length > 0)
                {
                    for (int i = 0; i < selectionIdsSorted.Length; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(selectionIdsSorted[i].ToString(CultureInfo.InvariantCulture));
                    }
                }

                using (var sha = SHA256.Create())
                {
                    var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                    var hash = sha.ComputeHash(bytes);
                    var hex = ToHexLower(hash);
                    // 128-bit prefix is enough and keeps tokens readable.
                    if (hex.Length > 32) hex = hex.Substring(0, 32);
                    return "ctx-" + hex;
                }
            }
            catch
            {
                return "ctx-error";
            }
        }

        private static string ToHexLower(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
                sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static string NormalizeForHash(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            try
            {
                return s.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            }
            catch
            {
                return (s ?? string.Empty).Trim().ToLowerInvariant();
            }
        }

        private static string GetDocGuid(Document doc)
        {
            try
            {
                string source;
                var docKey = DocumentKeyUtil.GetDocKeyOrStable(doc, createIfMissing: true, out source);
                if (!string.IsNullOrWhiteSpace(docKey)) return docKey.Trim();
            }
            catch { /* ignore */ }

            try { return doc?.ProjectInformation?.UniqueId ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string GetDocPath(Document doc)
        {
            try { return doc?.PathName ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static T? SafeGet<T>(Func<T> getter) where T : class
        {
            try { return getter(); } catch { return null; }
        }
    }
}
