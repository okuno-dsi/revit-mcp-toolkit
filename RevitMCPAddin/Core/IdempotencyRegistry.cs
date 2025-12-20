// Simple in-memory idempotency registry for short-lived duplicate suppression.
// Maps client-supplied idempotencyKey -> Revit element UniqueId.
// Note: process-local; cleared on Add-in reload/Revit restart.
#nullable enable
using System;
using System.Collections.Concurrent;

namespace RevitMCPAddin.Core
{
    internal static class IdempotencyRegistry
    {
        private static readonly ConcurrentDictionary<string, string> _map = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        public static bool TryGet(string key, out string? uniqueId)
        {
            uniqueId = null;
            if (string.IsNullOrWhiteSpace(key)) return false;
            if (_map.TryGetValue(key, out var v))
            {
                uniqueId = v;
                return true;
            }
            return false;
        }

        public static void Set(string key, string uniqueId)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (string.IsNullOrWhiteSpace(uniqueId)) return;
            _map[key] = uniqueId;
        }

        public static void Clear() => _map.Clear();
    }
}
