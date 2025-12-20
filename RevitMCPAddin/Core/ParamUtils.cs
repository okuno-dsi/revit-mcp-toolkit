// ================================================================
// File: Core/ParamUtils.cs
// Purpose: リクエスト params の "別名" → "正規キー" への一括正規化
// Target : .NET Framework 4.8 / C# 8
// ================================================================
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    internal static class ParamUtils
    {
        // 共通: elementId の別名候補（部屋/エリア/スペース/壁/床/天井/屋根/窓/ドア/柱/タグ/通り芯/RC等）
        private static readonly string[] ElementIdAliasesDefault = new[]
        {
            "elementId", "roomId", "areaId", "spaceId",
            "wallId", "floorId", "ceilingId", "roofId",
            "windowId", "doorId", "columnId", "tagId",
            "gridId", "revisionCloudId", "hostElementId"
        };

        // 共通: typeId の別名候補（壁/床/天井/屋根/窓/ドア/柱/マリオン/パネルなど）
        private static readonly string[] TypeIdAliasesDefault = new[]
        {
            "typeId", "wallTypeId", "floorTypeId", "ceilingTypeId", "roofTypeId",
            "windowTypeId", "doorTypeId", "columnTypeId", "mullionTypeId", "panelTypeId"
        };

        // ViewId の別名（uniqueId を受ける系は別扱い）
        private static readonly string[] ViewIdAliasesDefault = new[]
        {
            "viewId", "ownerViewId", "templateViewId"
        };

        /// <summary>
        /// elementId 正規化。elementId 未指定なら、既知の別名から最初に見つかった値を elementId として採用する。
        /// </summary>
        public static string NormalizeElementIdAliases(JObject p, params string[] extraAliases)
        {
            if (p == null) return null;
            if (p["elementId"] != null && p["elementId"].Type != JTokenType.Null) return null;

            var bag = new List<string>(ElementIdAliasesDefault);
            if (extraAliases != null && extraAliases.Length > 0) bag.AddRange(extraAliases);

            foreach (var key in bag)
            {
                if (string.Equals(key, "elementId", StringComparison.OrdinalIgnoreCase)) continue;
                var tok = p[key];
                if (tok != null && tok.Type != JTokenType.Null)
                {
                    p["elementId"] = tok;
                    return key; // どの別名を採用したか戻す
                }
            }
            return null;
        }

        /// <summary>
        /// typeId 正規化。typeId 未指定なら、既知の別名から転写する。
        /// </summary>
        public static string NormalizeTypeIdAliases(JObject p, params string[] extraAliases)
        {
            if (p == null) return null;
            if (p["typeId"] != null && p["typeId"].Type != JTokenType.Null) return null;

            var bag = new List<string>(TypeIdAliasesDefault);
            if (extraAliases != null && extraAliases.Length > 0) bag.AddRange(extraAliases);

            foreach (var key in bag)
            {
                if (string.Equals(key, "typeId", StringComparison.OrdinalIgnoreCase)) continue;
                var tok = p[key];
                if (tok != null && tok.Type != JTokenType.Null)
                {
                    p["typeId"] = tok;
                    return key;
                }
            }
            return null;
        }

        /// <summary>
        /// viewId 正規化（テンプレートIDやOwnerViewIdなど、ビューID系を viewId に寄せる）。
        /// </summary>
        public static string NormalizeViewIdAliases(JObject p, params string[] extraAliases)
        {
            if (p == null) return null;
            if (p["viewId"] != null && p["viewId"].Type != JTokenType.Null) return null;

            var bag = new List<string>(ViewIdAliasesDefault);
            if (extraAliases != null && extraAliases.Length > 0) bag.AddRange(extraAliases);

            foreach (var key in bag)
            {
                if (string.Equals(key, "viewId", StringComparison.OrdinalIgnoreCase)) continue;
                var tok = p[key];
                if (tok != null && tok.Type != JTokenType.Null)
                {
                    p["viewId"] = tok;
                    return key;
                }
            }
            return null;
        }

        /// <summary>
        /// 汎用: 指定キーが無い場合、候補キー群の最初に見つかった値をコピーする。
        /// </summary>
        public static string NormalizeKey(JObject p, string canonicalKey, params string[] aliasKeys)
        {
            if (p == null || string.IsNullOrEmpty(canonicalKey) || aliasKeys == null) return null;
            if (p[canonicalKey] != null && p[canonicalKey].Type != JTokenType.Null) return null;

            foreach (var ak in aliasKeys)
            {
                var tok = p[ak];
                if (tok != null && tok.Type != JTokenType.Null)
                {
                    p[canonicalKey] = tok;
                    return ak;
                }
            }
            return null;
        }
    }
}
