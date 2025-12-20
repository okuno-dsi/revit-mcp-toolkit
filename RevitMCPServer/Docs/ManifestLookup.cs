// File: RevitMcpServer/Docs/ManifestLookup.cs
#nullable enable
using System;
using System.Linq;

namespace RevitMcpServer.Docs
{
    public static class ManifestLookup
    {
        public static bool ContainsMethod(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            try
            {
                var all = ManifestRegistry.GetAll();
                return all.Any(m => string.Equals(m?.Name, name, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }
    }
}

