// ================================================================
// File: Core/UnitSettings.cs
// Purpose: アドイン全体の単位ポリシー設定（保存/読み込み）
// Note   : DataContract 系は使わず Newtonsoft.Json に統一（.NET 4.8 / Revit 2023）
// ================================================================
using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace RevitMCPAddin.Core
{
    public enum UnitsMode
    {
        SI,         // Length=mm, Area=m2, Volume=m3, Angle=deg
        Project,    // Project display units
        Raw,        // Revit internal (ft,ft2,ft3,rad)
        Both        // SI + Project (両方)
    }

    public sealed class UnitSettings
    {
        [JsonProperty(Order = 0)]
        public UnitsMode DefaultMode { get; set; } = UnitsMode.SI;

        // 追加オプション例（必要になったらコメントアウト解除）:
        // [JsonProperty(Order = 1)] public int SiDigits { get; set; } = 3;
        // [JsonProperty(Order = 2)] public bool IncludeRaw { get; set; } = true;

        public static string GetSettingsPath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RevitMCP"
            );
            try { Directory.CreateDirectory(dir); } catch { /* best-effort */ }
            return Path.Combine(dir, "settings.json");
        }

        public static UnitSettings Load()
        {
            var path = GetSettingsPath();
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path, Encoding.UTF8);
                    var obj = JsonConvert.DeserializeObject<UnitSettings>(json);
                    if (obj != null) return obj;
                }
            }
            catch (Exception ex)
            {
                RevitMCPAddin.Core.RevitLogger.Warn($"Unit settings load failed; using defaults: {ex.Message}");
            }

            return new UnitSettings();
        }

        public void Save()
        {
            var path = GetSettingsPath();
            try
            {
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                RevitMCPAddin.Core.RevitLogger.Warn($"Unit settings save failed: {ex.Message}");
            }
        }
    }

    /// <summary>プロセス存続中の設定キャッシュ</summary>
    public static class UnitSettingsManager
    {
        private static UnitSettings _settings = UnitSettings.Load();
        public static UnitSettings Current => _settings;

        public static void Reload() { _settings = UnitSettings.Load(); }

        public static void UpdateDefaultMode(UnitsMode mode)
        {
            _settings.DefaultMode = mode;
            _settings.Save();
        }
    }
}
