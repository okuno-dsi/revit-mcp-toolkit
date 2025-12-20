// ================================================================
// File: Core/VersionUtils.cs
// Revit メジャーバージョン判定ユーティリティ
// ================================================================
#nullable enable
using System;
using Autodesk.Revit.UI;

namespace RevitMCPAddin.Core
{
    public static class VersionUtils
    {
        /// <summary>Revit のメジャーバージョン（例: 2023, 2024）を返す。取得失敗時は 0。</summary>
        public static int GetRevitMajorVersion(UIApplication uiapp)
        {
            try
            {
                var s = uiapp?.Application?.VersionNumber;
                if (int.TryParse(s, out var v)) return v;
            }
            catch (Exception ex)
            {
                RevitMCPAddin.Core.RevitLogger.Warn($"GetRevitMajorVersion failed: {ex.Message}");
            }
            return 0;
        }

        /// <summary>指定バージョン以上か判定</summary>
        public static bool IsAtLeast(UIApplication uiapp, int major)
            => GetRevitMajorVersion(uiapp) >= major;
    }
}
