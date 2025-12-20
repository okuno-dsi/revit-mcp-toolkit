using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    public static class ResultUtil
    {
        public static JObject Ok(object? payload = null)
        {
            var root = new JObject { ["ok"] = true };
            if (payload != null)
            {
                if (payload is JObject jobj)
                    root.Merge(jobj); // 既存仕様踏襲
                else
                    root["result"] = JToken.FromObject(payload);
            }
            return root;
        }

        public static JObject Err(string msg, string? code = null)
        {
            var error = new JObject
            {
                ["ok"] = false,
                ["msg"] = msg
            };
            if (!string.IsNullOrEmpty(code))
                error["code"] = code;
            return error;
        }

        // === ここから追加（既存と矛盾なし） =========================
        public static JObject Err(object payload)
        {
            var root = new JObject { ["ok"] = false };

            if (payload != null)
            {
                if (payload is JObject jobj)
                {
                    // payload 側に ok があっても最終値は false に固定したいので、一旦消す
                    jobj.Remove("ok");
                    root.Merge(jobj);
                }
                else
                {
                    var obj = JObject.FromObject(payload);
                    obj.Remove("ok");
                    root.Merge(obj);
                }
            }

            // 念のため最終的に ok=false を上書き固定
            root["ok"] = false;
            return root;
        }
        // ============================================================
    }
}
