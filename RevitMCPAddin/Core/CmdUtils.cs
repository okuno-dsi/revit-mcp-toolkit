#nullable enable
using System;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    /// <summary>
    /// JSON パラメータから Element を引く最小限のユーティリティ（C# 8 対応）
    /// 受け付けるキー:
    ///  - elementId : int / long / string (数値文字列)
    ///  - uniqueId  : string
    /// </summary>
    public static class CmdUtils
    {
        public static Element GetElementByIdOrUniqueId(Document doc, JObject p)
        {
            if (doc == null || p == null) return null;

            // 1) uniqueId 優先
            JToken ujt = p["uniqueId"];
            if (ujt != null)
            {
                string uid = ujt.Type == JTokenType.String ? (string)ujt : null;
                if (!string.IsNullOrWhiteSpace(uid))
                {
                    try
                    {
                        var byUid = doc.GetElement(uid);
                        if (byUid != null) return byUid;
                    }
                    catch { /* ignore */ }
                }
            }

            // 2) elementId (int / long / string)
            JToken ejt = p["elementId"];
            if (ejt != null)
            {
                try
                {
                    int intId;
                    if (ejt.Type == JTokenType.Integer)
                    {
                        intId = (int)ejt;
                    }
                    else
                    {
                        // -1002003 のような負値も文字列で来る想定
                        string s = ejt.ToString();
                        if (!int.TryParse(s, out intId))
                            return null;
                    }
                    var id = new ElementId(intId);
                    var byId = doc.GetElement(id);
                    if (byId != null) return byId;
                }
                catch { /* ignore */ }
            }

            return null;
        }
    }
}
