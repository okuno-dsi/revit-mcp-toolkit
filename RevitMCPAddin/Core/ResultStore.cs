#nullable enable
using System.Collections.Concurrent;

namespace RevitMCPAddin.Core
{
    internal static class ResultStore
    {
        private static readonly ConcurrentDictionary<string, object> _last = new ConcurrentDictionary<string, object>();
        public static void Put(string jobId, object payload) => _last[jobId] = payload;
        public static bool TryGet(string jobId, out object payload) => _last.TryGetValue(jobId, out payload!);
    }
}
