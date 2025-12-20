#nullable enable
using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core.ViewWorkspace
{
    internal sealed class ViewWorkspaceSettings
    {
        public bool AutoRestoreEnabled { get; set; } = true;
        public bool AutosaveEnabled { get; set; } = true;
        public int AutosaveIntervalMinutes { get; set; } = 5;
        public int Retention { get; set; } = 10;
        public bool IncludeZoom { get; set; } = true;
        public bool Include3dOrientation { get; set; } = true;

        public static ViewWorkspaceSettings Load()
        {
            var s = new ViewWorkspaceSettings();
            try
            {
                var path = SettingsHelper.EnsureSettingsFile();
                if (!File.Exists(path)) return s;

                var root = JObject.Parse(File.ReadAllText(path));
                var vw = root["viewWorkspace"] as JObject;
                if (vw == null) return s;

                s.AutoRestoreEnabled = vw.Value<bool?>("autoRestoreEnabled") ?? s.AutoRestoreEnabled;
                s.AutosaveEnabled = vw.Value<bool?>("autosaveEnabled") ?? s.AutosaveEnabled;
                s.AutosaveIntervalMinutes = vw.Value<int?>("autosaveIntervalMinutes") ?? s.AutosaveIntervalMinutes;
                s.Retention = vw.Value<int?>("retention") ?? s.Retention;
                s.IncludeZoom = vw.Value<bool?>("includeZoom") ?? s.IncludeZoom;
                s.Include3dOrientation = vw.Value<bool?>("include3dOrientation") ?? s.Include3dOrientation;
            }
            catch { }

            // sanitize
            if (s.AutosaveIntervalMinutes < 1) s.AutosaveIntervalMinutes = 1;
            if (s.Retention < 1) s.Retention = 1;
            if (s.Retention > 50) s.Retention = 50;

            return s;
        }

        public void Save()
        {
            try
            {
                var path = SettingsHelper.EnsureSettingsFile();
                JObject root;
                try
                {
                    root = JObject.Parse(File.ReadAllText(path));
                }
                catch
                {
                    root = new JObject();
                }

                var vw = root["viewWorkspace"] as JObject ?? new JObject();
                vw["autoRestoreEnabled"] = AutoRestoreEnabled;
                vw["autosaveEnabled"] = AutosaveEnabled;
                vw["autosaveIntervalMinutes"] = AutosaveIntervalMinutes;
                vw["retention"] = Retention;
                vw["includeZoom"] = IncludeZoom;
                vw["include3dOrientation"] = Include3dOrientation;
                root["viewWorkspace"] = vw;

                File.WriteAllText(path, root.ToString());
            }
            catch { }
        }
    }
}
