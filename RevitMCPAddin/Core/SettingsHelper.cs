#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    internal static class SettingsHelper
    {
        public static string EnsureSettingsFile()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitMCP");
            var path = Path.Combine(dir, "settings.json");
            Directory.CreateDirectory(dir);
            if (!File.Exists(path))
            {
                var jo = new JObject
                {
                    ["ai"] = new JObject { ["provider"] = "none", ["model"] = "" },
                    ["server"] = new JObject { ["port"] = 5210 },
                    // View workspace save/restore defaults
                    ["viewWorkspace"] = new JObject
                    {
                        ["autoRestoreEnabled"] = true,
                        ["autosaveEnabled"] = true,
                        ["autosaveIntervalMinutes"] = 5,
                        ["retention"] = 10,
                        ["includeZoom"] = true,
                        ["include3dOrientation"] = true
                    }
                };
                File.WriteAllText(path, jo.ToString());
            }
            else
            {
                // Minimal migration: ensure viewWorkspace defaults exist (non-destructive).
                try
                {
                    var root = JObject.Parse(File.ReadAllText(path));
                    var vw = root["viewWorkspace"] as JObject;
                    if (vw == null)
                    {
                        vw = new JObject();
                        root["viewWorkspace"] = vw;
                    }

                    if (vw["autoRestoreEnabled"] == null) vw["autoRestoreEnabled"] = true;
                    if (vw["autosaveEnabled"] == null) vw["autosaveEnabled"] = true;
                    if (vw["autosaveIntervalMinutes"] == null) vw["autosaveIntervalMinutes"] = 5;
                    if (vw["retention"] == null) vw["retention"] = 10;
                    if (vw["includeZoom"] == null) vw["includeZoom"] = true;
                    if (vw["include3dOrientation"] == null) vw["include3dOrientation"] = true;

                    File.WriteAllText(path, root.ToString());
                }
                catch
                {
                    // ignore
                }
            }
            return path;
        }

        public static void OpenInNotepad()
        {
            var path = EnsureSettingsFile();
            try { Process.Start("notepad.exe", path); }
            catch
            {
                // Fallback
                Process.Start(path);
            }
        }
    }
}
