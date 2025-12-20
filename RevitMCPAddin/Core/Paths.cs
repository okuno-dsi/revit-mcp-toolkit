// Core/Paths.cs
#nullable enable
using System;
using System.IO;
using System.Reflection;

namespace RevitMCPAddin.Core
{
    public static class Paths
    {
        public static string AddinFolder
        {
            get
            {
                var asm = Assembly.GetExecutingAssembly().Location;
                var dir = Path.GetDirectoryName(asm);
                return string.IsNullOrEmpty(dir) ? AppDomain.CurrentDomain.BaseDirectory : dir!;
            }
        }

        /// <summary>%LOCALAPPDATA%\RevitMCP</summary>
        public static string LocalRoot
        {
            get
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(local, "RevitMCP");
            }
        }

        /// <summary>%LOCALAPPDATA%\RevitMCP\logs（存在しなければ作成）</summary>
        public static string EnsureLocalLogs()
        {
            var p = Path.Combine(LocalRoot, "logs");
            if (!Directory.Exists(p)) Directory.CreateDirectory(p);
            return p;
        }

        /// <summary>%LOCALAPPDATA%\RevitMCP\locks（存在しなければ作成）</summary>
        public static string EnsureLocalLocks()
        {
            var p = Path.Combine(LocalRoot, "locks");
            if (!Directory.Exists(p)) Directory.CreateDirectory(p);
            return p;
        }

        // （参考）アドイン配下の logs が必要な場合だけ使う
        public static string EnsureAddinLogs()
        {
            var p = Path.Combine(AddinFolder, "logs");
            if (!Directory.Exists(p)) Directory.CreateDirectory(p);
            return p;
        }
    }
}
