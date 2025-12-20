using System;
using Microsoft.Extensions.Configuration;

namespace AutoCadMcpServer.Core
{
    /// <summary>
    /// Centralized path validation. Allows opt-in UNC via config:
    ///   "PathGuard:AllowUNC": true|false (default false)
    ///   "PathGuard:AllowedDrives": ["C","D"] (fallback to root "AllowedDrives" too)
    /// </summary>
    public static class PathGuard
    {
        public static void EnsureAllowedDwg(string path, IConfiguration config)
            => EnsureAllowed(path, config, expectExt: ".dwg");

        public static void EnsureAllowedOutput(string path, IConfiguration config)
            => EnsureAllowed(path, config, expectExt: ".dwg");

        private static void EnsureAllowed(string path, IConfiguration config, string expectExt)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new Router.RpcError(400, "E_PATH_EMPTY");

            var ext = System.IO.Path.GetExtension(path)?.ToLowerInvariant();
            if (!string.Equals(ext, expectExt, StringComparison.OrdinalIgnoreCase))
                throw new Router.RpcError(400, $"E_EXT_MISMATCH: expected {expectExt}");

            // UNC
            var allowUNC = config.GetValue<bool?>("PathGuard:AllowUNC")
                         ?? config.GetValue<bool?>("AllowUNC")
                         ?? false;
            if (path.StartsWith(@"\\") && !allowUNC)
                throw new Router.RpcError(400, "E_PATH_DENY: UNC not allowed");

            // normalization + traversal check
            var full = System.IO.Path.GetFullPath(path);
            var sep = System.IO.Path.DirectorySeparatorChar;
            if (full.Contains(".." + sep))
                throw new Router.RpcError(400, "E_PATH_DENY: traversal");

            // drive allowlist
            var drives = config.GetSection("PathGuard:AllowedDrives").Get<string[]>() 
                      ?? config.GetSection("AllowedDrives").Get<string[]>() 
                      ?? new [] { "C" };

            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in drives)
            {
                if (string.IsNullOrWhiteSpace(d)) continue;
                allowed.Add(d.Trim().TrimEnd(':').Replace("\\","/").TrimEnd('/'));
            }

            var drive = System.IO.Path.GetPathRoot(full) ?? string.Empty;
            var driveKey = drive.Replace("\\", "/").TrimEnd('/').TrimEnd(':');
            if (string.IsNullOrEmpty(driveKey) || !allowed.Contains(driveKey))
                throw new Router.RpcError(400, $"E_PATH_DENY: drive not allowed: {drive}");
        }
    }
}
