#nullable enable
// ================================================================
// File   : Core/IdempotencyResultCache.cs
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8.0
// Purpose:
//   - Short-lived in-memory cache for idempotent RPC results.
//   - Suppresses duplicate execution when clients retry.
// ================================================================
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    internal static class IdempotencyResultCache
    {
        private sealed class Entry
        {
            public JObject Payload { get; set; } = new JObject();
            public JToken? Ledger { get; set; }
            public DateTime ExpiresUtc { get; set; }
            public DateTime CreatedUtc { get; set; }
        }

        private static readonly ConcurrentDictionary<string, Entry> _map
            = new ConcurrentDictionary<string, Entry>(StringComparer.Ordinal);

        private const int MaxEntries = 256;
        private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(2);

        public static bool TryGet(string key, out JObject? payload, out JToken? ledger)
        {
            payload = null;
            ledger = null;
            if (string.IsNullOrWhiteSpace(key)) return false;

            CleanupIfNeeded();
            if (_map.TryGetValue(key, out var entry))
            {
                if (entry.ExpiresUtc <= DateTime.UtcNow)
                {
                    _map.TryRemove(key, out _);
                    return false;
                }
                // Return deep clones to avoid cross-call mutation.
                payload = entry.Payload.DeepClone() as JObject ?? new JObject();
                ledger = entry.Ledger != null ? entry.Ledger.DeepClone() : null;
                return true;
            }
            return false;
        }

        public static void Set(string key, JObject payload, object? ledger, TimeSpan? ttl = null)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (payload == null) return;

            var entry = new Entry
            {
                Payload = payload.DeepClone() as JObject ?? new JObject(),
                Ledger = ledger == null ? null : (ledger is JToken jt ? jt.DeepClone() : JToken.FromObject(ledger)),
                CreatedUtc = DateTime.UtcNow,
                ExpiresUtc = DateTime.UtcNow.Add(ttl ?? DefaultTtl)
            };
            _map[key] = entry;
            CleanupIfNeeded();
        }

        private static void CleanupIfNeeded()
        {
            try
            {
                var now = DateTime.UtcNow;
                foreach (var kv in _map)
                {
                    if (kv.Value.ExpiresUtc <= now)
                        _map.TryRemove(kv.Key, out _);
                }

                if (_map.Count <= MaxEntries) return;

                // Remove oldest entries when exceeding cap.
                var ordered = _map
                    .OrderBy(kv => kv.Value.ExpiresUtc)
                    .Take(Math.Max(1, _map.Count - MaxEntries))
                    .Select(kv => kv.Key)
                    .ToList();
                foreach (var k in ordered) _map.TryRemove(k, out _);
            }
            catch { /* ignore */ }
        }
    }
}
