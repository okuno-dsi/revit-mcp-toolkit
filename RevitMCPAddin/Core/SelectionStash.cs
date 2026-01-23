#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RevitMCPAddin.Core
{
    /// <summary>
    /// Immutable snapshot of the last known selection, with context.
    /// </summary>
    internal sealed class SelectionSnapshot
    {
        public int[] Ids { get; set; } = Array.Empty<int>();
        public DateTime ObservedUtc { get; set; } = DateTime.MinValue;
        public string DocPath { get; set; } = string.Empty;
        public string DocTitle { get; set; } = string.Empty;
        public string DocKey { get; set; } = string.Empty;
        public int ActiveViewId { get; set; } = 0;
        public long Revision { get; set; } = 0;
        public string Hash { get; set; } = string.Empty;
    }

    internal static class SelectionStash
    {
        private static readonly object _lock = new object();
        private static SelectionSnapshot _snap = new SelectionSnapshot();
        private static SelectionSnapshot _lastNonEmptySnap = new SelectionSnapshot();
        private static long _rev = 0;

        private static string ComputeHash(int[] ids)
        {
            try
            {
                var sorted = (ids ?? Array.Empty<int>()).OrderBy(x => x).ToArray();
                var bytes = Encoding.UTF8.GetBytes(string.Join(",", sorted));
                using var sha1 = SHA1.Create();
                var h = sha1.ComputeHash(bytes);
                return BitConverter.ToString(h).Replace("-", string.Empty);
            }
            catch { return string.Empty; }
        }

        public static void Set(IEnumerable<int> ids, string docPath = null, string docTitle = null, int activeViewId = 0, string docKey = null)
        {
            lock (_lock)
            {
                var arr = (ids ?? Array.Empty<int>()).ToArray();
                _rev++;
                var next = new SelectionSnapshot
                {
                    Ids = arr,
                    ObservedUtc = DateTime.UtcNow,
                    DocPath = docPath ?? _snap.DocPath ?? string.Empty,
                    DocTitle = docTitle ?? _snap.DocTitle ?? string.Empty,
                    DocKey = docKey ?? _snap.DocKey ?? string.Empty,
                    ActiveViewId = activeViewId,
                    Revision = _rev,
                    Hash = ComputeHash(arr)
                };
                _snap = next;

                if (arr.Length > 0)
                {
                    _lastNonEmptySnap = new SelectionSnapshot
                    {
                        Ids = arr.ToArray(),
                        ObservedUtc = next.ObservedUtc,
                        DocPath = next.DocPath,
                        DocTitle = next.DocTitle,
                        DocKey = next.DocKey,
                        ActiveViewId = next.ActiveViewId,
                        Revision = next.Revision,
                        Hash = next.Hash
                    };
                }
            }
        }

        public static int[] GetIds()
        {
            lock (_lock) { return _snap.Ids?.ToArray() ?? Array.Empty<int>(); }
        }

        public static SelectionSnapshot GetSnapshot()
        {
            lock (_lock)
            {
                return new SelectionSnapshot
                {
                    Ids = _snap.Ids?.ToArray() ?? Array.Empty<int>(),
                    ObservedUtc = _snap.ObservedUtc,
                    DocPath = _snap.DocPath,
                    DocTitle = _snap.DocTitle,
                    DocKey = _snap.DocKey,
                    ActiveViewId = _snap.ActiveViewId,
                    Revision = _snap.Revision,
                    Hash = _snap.Hash
                };
            }
        }

        public static SelectionSnapshot GetLastNonEmptySnapshot()
        {
            lock (_lock)
            {
                return new SelectionSnapshot
                {
                    Ids = _lastNonEmptySnap.Ids?.ToArray() ?? Array.Empty<int>(),
                    ObservedUtc = _lastNonEmptySnap.ObservedUtc,
                    DocPath = _lastNonEmptySnap.DocPath,
                    DocTitle = _lastNonEmptySnap.DocTitle,
                    DocKey = _lastNonEmptySnap.DocKey,
                    ActiveViewId = _lastNonEmptySnap.ActiveViewId,
                    Revision = _lastNonEmptySnap.Revision,
                    Hash = _lastNonEmptySnap.Hash
                };
            }
        }

        public static int[] GetLastNonEmptyIds()
        {
            lock (_lock) { return _lastNonEmptySnap.Ids?.ToArray() ?? Array.Empty<int>(); }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _snap = new SelectionSnapshot();
                _lastNonEmptySnap = new SelectionSnapshot();
            }
        }

        public static DateTime TimestampUtc()
        {
            lock (_lock) { return _snap.ObservedUtc; }
        }
    }
}
