#nullable enable
using System;
using System.Linq;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCP.Abstractions.Models;

namespace RevitMCPAddin.Core
{
    public static class ResponseNormalizer
    {
        public static object Normalize(UIApplication uiapp, RequestCommand cmd, object result)
        {
            if (result == null) return new { ok = false, error = new ErrorInfo { Code = "NO_RESULT", HumanMessage = "結果が空です。" } };

            JToken token;
            try { token = JToken.FromObject(result); }
            catch
            {
                // 直列化できない戻りは例外扱い（素直にテキスト化）
                return new
                {
                    ok = false,
                    error = new ErrorInfo
                    {
                        Code = "SERIALIZE_ERROR",
                        HumanMessage = "結果のシリアライズに失敗しました。",
                        Details = new { resultType = result.GetType().FullName }
                    }
                };
            }

            // 1) ルートが配列の場合は包む（既存との互換のため、崩さず data に入れる）
            if (token is JArray arrRoot)
            {
                var root = new JObject
                {
                    ["ok"] = true,
                    ["data"] = arrRoot
                };
                AttachUnits(cmd, root);
                return root.ToObject<object>()!;
            }

            // 2) ルートがオブジェクトの場合
            var obj = token as JObject ?? new JObject { ["data"] = token };

            // ok を補完
            if (!obj.TryGetValue("ok", out var okTok))
                obj["ok"] = true;

            // error 正規化（ok=false のとき）
            if (obj.TryGetValue("ok", out okTok) && okTok.Type == JTokenType.Boolean && okTok.Value<bool>() == false)
            {
                // 既存の errorCode / errorHint / msg / humanMessage を束ねて error に入れる（元は残す）
                var code = obj.TryGetValue("errorCode", out var c) ? c.Value<string>() : null;
                var hint = obj.TryGetValue("errorHint", out var h) ? h.Value<string>() : null;
                var human = obj.TryGetValue("humanMessage", out var hm) ? hm.Value<string>()
                           : obj.TryGetValue("msg", out var m) ? m.Value<string>()
                           : null;

                var error = new ErrorInfo { Code = code, Hint = hint, HumanMessage = human };
                obj["error"] = JToken.FromObject(error);
            }

            // 3) 単位を補完（inputUnits は params から拾う or 既定値、internalUnits は ft/rad）
            AttachUnits(cmd, obj);

            // 4) そのまま返す（元のフィールドは壊さない＝後方互換）
            return obj.ToObject<object>()!;
        }

        private static void AttachUnits(RequestCommand cmd, JObject root)
        {
            // inputUnits: params.inputUnits を最優先、なければ mm/deg
            UnitsSpec input = new UnitsSpec { Length = "mm", Angle = "deg" };
            var p = cmd?.Params;
            if (p != null && p.TryGetValue("inputUnits", out var uTok) && uTok is JObject uObj)
            {
                var len = uObj.Value<string>("length");
                var ang = uObj.Value<string>("angle");
                if (!string.IsNullOrWhiteSpace(len)) input.Length = len;
                if (!string.IsNullOrWhiteSpace(ang)) input.Angle = ang;
            }
            if (!root.ContainsKey("inputUnits"))
                root["inputUnits"] = JToken.FromObject(input);

            // internalUnits: Revit内部は ft/rad を明記
            UnitsSpec internalUnits = new UnitsSpec { Length = "ft", Angle = "rad" };
            if (!root.ContainsKey("internalUnits"))
                root["internalUnits"] = JToken.FromObject(internalUnits);
        }

        // 例外→標準エラーに変換したい時に使う補助（必要なら Executor 側の catch で呼ぶ）
        public static object FromException(Exception ex)
        {
            return new
            {
                ok = false,
                error = new ErrorInfo
                {
                    Code = "EXCEPTION",
                    HumanMessage = "例外が発生しました。",
                    Details = new { ex.Message, ex.StackTrace }
                }
            };
        }
    }
}
