// ============================================================================
// File: Core/RevitLinkTypeExtensions.cs
// 概要：RevitLinkType のロード状態取得をバージョン差吸収で安全に行う
//  - TryGetIsLoaded(RevitLinkType, out bool isLoaded)
//     * IsLoaded プロパティ or メソッドに対応（反射）
//     * 退避策として LinkedFileStatus を使用
// ============================================================================

using System;
using System.Reflection;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Core
{
    public static class RevitLinkTypeExtensions
    {
        public static bool TryGetIsLoaded(RevitLinkType linkType, out bool isLoaded)
        {
            isLoaded = false;
            if (linkType == null) return false;

            // 1) プロパティ IsLoaded
            var prop = linkType.GetType().GetProperty("IsLoaded", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.PropertyType == typeof(bool))
            {
                try
                {
                    isLoaded = (bool)prop.GetValue(linkType);
                    return true;
                }
                catch { /* fallthrough */ }
            }

            // 2) メソッド IsLoaded()
            var method = linkType.GetType().GetMethod("IsLoaded", BindingFlags.Public | BindingFlags.Instance, Type.DefaultBinder, Type.EmptyTypes, null);
            if (method != null && method.ReturnType == typeof(bool))
            {
                try
                {
                    isLoaded = (bool)method.Invoke(linkType, null);
                    return true;
                }
                catch { /* fallthrough */ }
            }

            // 3) フォールバック：LinkedFileStatus 判定
            try
            {
                var status = linkType.GetLinkedFileStatus();
                // LinkedFileStatus は Enum。Loaded / Unloaded / NotFound など
                isLoaded = status.ToString().IndexOf("Loaded", StringComparison.OrdinalIgnoreCase) >= 0;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
