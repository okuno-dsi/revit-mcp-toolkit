#nullable enable
// ================================================================
// File   : Core/DocumentKeyUtil.cs
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8.0
// Purpose:
//   - Provide a stable document key even when Ledger DocKey is unavailable.
//   - Prefer Ledger DocKey when available; fallback to stable hash.
// ================================================================
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using RevitMCPAddin.Core.Ledger;

namespace RevitMCPAddin.Core
{
    internal static class DocumentKeyUtil
    {
        /// <summary>
        /// Prefer MCP Ledger DocKey when available; otherwise return a stable hash key.
        /// </summary>
        public static string GetDocKeyOrStable(Document doc, bool createIfMissing, out string source)
        {
            source = "unknown";
            if (doc == null) return string.Empty;

            try
            {
                if (LedgerDocKeyProvider.TryGetDocKey(doc, out var dk, out _, out _)
                    && !string.IsNullOrWhiteSpace(dk))
                {
                    source = "ledger";
                    return dk.Trim();
                }
                if (createIfMissing
                    && LedgerDocKeyProvider.TryGetOrCreateDocKey(doc, createIfMissing: true, out dk, out _, out _)
                    && !string.IsNullOrWhiteSpace(dk))
                {
                    source = "ledger";
                    return dk.Trim();
                }
            }
            catch { /* ignore */ }

            return GetStableDocKey(doc, out source);
        }

        /// <summary>
        /// Always compute a stable hash-based doc key from path/title/project UniqueId.
        /// </summary>
        public static string GetStableDocKey(Document doc, out string source)
        {
            source = "stable";
            if (doc == null) return string.Empty;

            try
            {
                string path = SafeGet(() => doc.PathName) ?? string.Empty;
                string title = SafeGet(() => doc.Title) ?? string.Empty;
                string projUid = SafeGet(() => doc.ProjectInformation?.UniqueId) ?? string.Empty;

                string central = string.Empty;
                try
                {
                    var mp = doc.GetWorksharingCentralModelPath();
                    if (mp != null)
                    {
                        var cp = ModelPathUtils.ConvertModelPathToUserVisiblePath(mp);
                        if (!string.IsNullOrWhiteSpace(cp)) central = cp;
                    }
                }
                catch { /* ignore */ }

                var input = string.Join("|", new[]
                {
                    Normalize(path),
                    Normalize(central),
                    Normalize(title),
                    Normalize(projUid)
                });
                if (string.IsNullOrWhiteSpace(input)) input = Normalize(title);

                var hex = Sha256Hex(input);
                if (hex.Length > 16) hex = hex.Substring(0, 16);
                return "doc-" + hex;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string Normalize(string s)
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

        private static string Sha256Hex(string s)
        {
            try
            {
                using (var sha = SHA256.Create())
                {
                    var bytes = Encoding.UTF8.GetBytes(s ?? string.Empty);
                    var hash = sha.ComputeHash(bytes);
                    var sb = new StringBuilder(hash.Length * 2);
                    for (int i = 0; i < hash.Length; i++)
                        sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                    return sb.ToString();
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static T? SafeGet<T>(Func<T> getter) where T : class
        {
            try { return getter(); } catch { return null; }
        }
    }
}
