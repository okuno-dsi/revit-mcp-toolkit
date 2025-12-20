// ================================================================
// File: Core/ResponseAutoFixer.cs
// 役割 : ハンドラの出力を正規化し、堅牢でない形式を補正する
// ================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    /// <summary>
    /// ハンドラ出力をチェックして安全な JSON レスポンスに変換する
    /// ResponseNormalizer と重複しないように別クラスとして実装
    /// </summary>
    public static class ResponseAutoFixer
    {
        /// <summary>
        /// 出力をチェックして正しい形式に修正
        /// </summary>
        public static string Fix(object? raw)
        {
            try
            {
                // すでに JSON 文字列ならそのまま返す
                if (raw is string s && s.TrimStart().StartsWith("{"))
                    return s;

                // JObject/JArray は ok:true でラップ
                if (raw is JObject jObj)
                    return JsonSerializer.Serialize(new { ok = true, result = jObj });
                if (raw is JArray jArr)
                    return JsonSerializer.Serialize(new { ok = true, result = jArr });

                // List や匿名型 → 一般 object としてラップ
                if (raw is IEnumerable && !(raw is string))
                    return JsonSerializer.Serialize(new { ok = true, result = raw });

                // null や空 → ok:false
                if (raw == null)
                    return JsonSerializer.Serialize(new { ok = false, issues = new[] { "handler returned null" } });

                // その他 → ok:true
                return JsonSerializer.Serialize(new { ok = true, result = raw });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { ok = false, issues = new[] { $"AutoFix failed: {ex.Message}" } });
            }
        }
    }
}
