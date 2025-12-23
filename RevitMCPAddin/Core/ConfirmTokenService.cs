#nullable enable
using System;
using System.Collections.Concurrent;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Core
{
    internal sealed class ConfirmTokenIssue
    {
        public string Token { get; set; } = string.Empty;
        public DateTimeOffset ExpiresAtUtc { get; set; }
        public int TtlSec { get; set; }
    }

    /// <summary>
    /// Ephemeral confirmation token store for high-risk commands.
    /// - Token is issued on dryRun and can be required for the real execution.
    /// - Tokens are one-time-use and expire quickly to avoid stale confirmations.
    /// </summary>
    internal static class ConfirmTokenService
    {
        private sealed class Entry
        {
            public string Method = string.Empty;
            public string DocGuid = string.Empty;
            public string Fingerprint = string.Empty;
            public DateTimeOffset ExpiresAtUtc;
        }

        private static readonly ConcurrentDictionary<string, Entry> _tokens =
            new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

        public static ConfirmTokenIssue Issue(string method, Document doc, string fingerprint, TimeSpan? ttl = null)
        {
            SweepExpired();

            var effectiveTtl = ttl ?? DefaultTtl;
            if (effectiveTtl <= TimeSpan.Zero) effectiveTtl = DefaultTtl;

            var expires = DateTimeOffset.UtcNow.Add(effectiveTtl);
            var token = "ct-" + Guid.NewGuid().ToString("N");

            _tokens[token] = new Entry
            {
                Method = (method ?? string.Empty).Trim(),
                DocGuid = SafeDocGuid(doc),
                Fingerprint = fingerprint ?? string.Empty,
                ExpiresAtUtc = expires
            };

            return new ConfirmTokenIssue
            {
                Token = token,
                ExpiresAtUtc = expires,
                TtlSec = (int)Math.Max(1, effectiveTtl.TotalSeconds)
            };
        }

        public static bool TryValidate(string token, string method, Document doc, string fingerprint, out string error)
        {
            error = string.Empty;
            SweepExpired();

            if (string.IsNullOrWhiteSpace(token))
            {
                error = "Missing confirmToken.";
                return false;
            }

            if (!_tokens.TryRemove(token.Trim(), out var entry) || entry == null)
            {
                error = "Unknown or expired confirmToken. Run with dryRun=true to get a fresh token.";
                return false;
            }

            if (DateTimeOffset.UtcNow > entry.ExpiresAtUtc)
            {
                error = "confirmToken expired. Run with dryRun=true to get a fresh token.";
                return false;
            }

            var m = (method ?? string.Empty).Trim();
            if (!string.Equals(entry.Method, m, StringComparison.OrdinalIgnoreCase))
            {
                error = "confirmToken method mismatch.";
                return false;
            }

            var docGuid = SafeDocGuid(doc);
            if (!string.Equals(entry.DocGuid ?? string.Empty, docGuid ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                error = "confirmToken document mismatch.";
                return false;
            }

            if (!string.Equals(entry.Fingerprint ?? string.Empty, fingerprint ?? string.Empty, StringComparison.Ordinal))
            {
                error = "confirmToken parameters mismatch.";
                return false;
            }

            return true;
        }

        private static void SweepExpired()
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var kv in _tokens)
                {
                    if (kv.Value == null) { _tokens.TryRemove(kv.Key, out _); continue; }
                    if (kv.Value.ExpiresAtUtc <= now)
                        _tokens.TryRemove(kv.Key, out _);
                }
            }
            catch { /* best-effort */ }
        }

        private static string SafeDocGuid(Document doc)
        {
            try { return doc?.ProjectInformation?.UniqueId ?? string.Empty; }
            catch { return string.Empty; }
        }
    }
}

