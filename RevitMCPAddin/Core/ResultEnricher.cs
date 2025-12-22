// ================================================================
// File: Core/ResultEnricher.cs  (配列アイテムまで uniqueId/elementId を自動補完する版)
// ================================================================
#nullable enable
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    public static class ResultEnricher
    {
        public static object WithIdentity(UIApplication uiapp, RequestCommand cmd, object result)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null || result == null) return result;

            JToken token;
            try { token = JToken.FromObject(result); }
            catch { return result; }

            // 1) 単一ターゲットの identity を付与（従来どおり）
            var identityElement = ResolveTargetElement(doc, cmd, token);
            if (identityElement != null)
            {
                // ★ 修正ポイント：UniqueId を渡す
                var idObj = ElementUtils.BuildIdentity(identityElement.Document, identityElement.UniqueId);
                if (idObj != null)
                {
                    if (token is JObject rootObj)
                        rootObj["identity"] = JToken.FromObject(idObj);
                    else
                        token = new JObject { ["ok"] = true, ["result"] = token, ["identity"] = JToken.FromObject(idObj) };
                }
            }

            // 2) 配列/オブジェクトの中まで再帰的に走査し、uniqueId/elementId を相互補完
            EnrichPerItemIds(doc, token);

            try { return token.ToObject<object>() ?? result; }
            catch { return result; }
        }

        private static Element? ResolveTargetElement(Document doc, RequestCommand cmd, JToken resultToken)
        {
            var p = cmd?.Params;
            if (p != null)
            {
                // 既存キー
                if (p.TryGetValue("uniqueId", out var uidTok) && uidTok.Type == JTokenType.String)
                {
                    var e = doc.GetElement(uidTok.Value<string>());
                    if (e != null) return e;
                }
                if (p.TryGetValue("elementId", out var eidTok) && eidTok.Type == JTokenType.Integer)
                {
                    var e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eidTok.Value<int>()));
                    if (e != null) return e;
                }
                if (p.TryGetValue("uniqueIds", out var uidsTok) && uidsTok is JArray uarr)
                {
                    var first = uarr.Values<string>().FirstOrDefault();
                    if (!string.IsNullOrEmpty(first))
                    {
                        var e = doc.GetElement(first);
                        if (e != null) return e;
                    }
                }
                if (p.TryGetValue("elementIds", out var eidsTok) && eidsTok is JArray earr)
                {
                    var first = earr.Values<int?>().FirstOrDefault();
                    if (first.HasValue)
                    {
                        var e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(first.Value));
                        if (e != null) return e;
                    }
                }

                // ★ 追加: ElementId 系の別名キーから代表要素を解決
                var altIdKeys = new[]
                {
                    "viewId", "typeId", "newTypeId",
                    "scheduleViewId", "sheetId", "revisionId",
                    "gridId", "levelId", "materialId"
                };
                foreach (var key in altIdKeys)
                {
                    if (p.TryGetValue(key, out var altTok) && altTok.Type == JTokenType.Integer)
                    {
                        var e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(altTok.Value<int>()));
                        if (e != null) return e;
                    }
                    // *_UniqueId にも将来対応（例: viewUniqueId など）
                    var uidKey = key.Replace("Id", "UniqueId");
                    if (p.TryGetValue(uidKey, out var altUidTok) && altUidTok.Type == JTokenType.String)
                    {
                        var e = doc.GetElement(altUidTok.Value<string>());
                        if (e != null) return e;
                    }
                }
            }

            // 応答側から代表要素を探す（配列の先頭など）
            var obj = resultToken as JObject;
            if (obj != null)
            {
                // 配列の中の最初の JObject から試行
                foreach (var key in obj.Properties().Select(pv => pv.Name))
                {
                    var v = obj[key];
                    if (v is JArray arr)
                    {
                        var first = arr.OfType<JObject>().FirstOrDefault();
                        var e = TryGetElementFromContainer(doc, first);
                        if (e != null) return e;
                    }
                }

                // 自身のオブジェクトからも試行
                var e2 = TryGetElementFromContainer(doc, obj);
                if (e2 != null) return e2;
            }

            return null;
        }

        private static void EnrichPerItemIds(Document doc, JToken token)
        {
            if (token == null) return;

            switch (token.Type)
            {
                case JTokenType.Object:
                    {
                        var obj = (JObject)token;
                        MapIdFieldsOnContainer(doc, obj);
                        foreach (var prop in obj.Properties())
                            EnrichPerItemIds(doc, prop.Value);
                        break;
                    }
                case JTokenType.Array:
                    {
                        var arr = (JArray)token;
                        foreach (var item in arr)
                            EnrichPerItemIds(doc, item);
                        break;
                    }
            }
        }

        private static void MapIdFieldsOnContainer(Document doc, JObject container)
        {
            // elementId → uniqueId を補完
            if (container.TryGetValue("elementId", out var eidTok) && eidTok.Type == JTokenType.Integer)
            {
                if (!container.ContainsKey("uniqueId"))
                {
                    var e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eidTok.Value<int>()));
                    if (e != null) container["uniqueId"] = e.UniqueId;
                }
            }

            // uniqueId → elementId を補完
            if (container.TryGetValue("uniqueId", out var uidTok) && uidTok.Type == JTokenType.String)
            {
                if (!container.ContainsKey("elementId"))
                {
                    var e = doc.GetElement(uidTok.Value<string>());
                    if (e != null) container["elementId"] = e.Id.IntValue();
                }
            }

            // ★ 追加: 配列 elementIds[int] → uniqueIds[string] を補完
            if (container.TryGetValue("elementIds", out var eidsTok) && eidsTok is JArray eidsArr)
            {
                if (!container.ContainsKey("uniqueIds"))
                {
                    var uids = new JArray();
                    foreach (var id in eidsArr.Values<int>())
                    {
                        var e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id));
                        if (e != null) uids.Add(e.UniqueId);
                    }
                    if (uids.Count > 0) container["uniqueIds"] = uids;
                }
            }

            // ★ 追加: 配列 uniqueIds[string] → elementIds[int] を補完
            if (container.TryGetValue("uniqueIds", out var uidsTok) && uidsTok is JArray uidsArr)
            {
                if (!container.ContainsKey("elementIds"))
                {
                    var ids = new JArray();
                    foreach (var uid in uidsArr.Values<string>())
                    {
                        if (string.IsNullOrWhiteSpace(uid)) continue;
                        var e = doc.GetElement(uid);
                        if (e != null) ids.Add(e.Id.IntValue());
                    }
                    if (ids.Count > 0) container["elementIds"] = ids;
                }
            }

            // ★ 追加: ElementId 系の別名キーを共通キー(elementId/uniqueId)へ正規化して補完
            var altIdKeys = new[]
            {
                "viewId", "typeId", "newTypeId",
                "scheduleViewId", "sheetId", "revisionId",
                "gridId", "levelId", "materialId"
            };

            for (int i = 0; i < altIdKeys.Length; i++)
            {
                var idKey = altIdKeys[i];
                // 別名の ElementId -> 共通 uniqueId / elementId
                if (container.TryGetValue(idKey, out var sidTok) && sidTok.Type == JTokenType.Integer)
                {
                    if (!container.ContainsKey("elementId"))
                        container["elementId"] = sidTok.DeepClone();

                    if (!container.ContainsKey("uniqueId"))
                    {
                        var e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(sidTok.Value<int>()));
                        if (e != null) container["uniqueId"] = e.UniqueId;
                    }
                }

                // *_UniqueId にも将来対応（例: viewUniqueId）
                var uidKey = idKey.Replace("Id", "UniqueId");
                if (container.TryGetValue(uidKey, out var suidTok) && suidTok.Type == JTokenType.String)
                {
                    if (!container.ContainsKey("uniqueId"))
                        container["uniqueId"] = suidTok.DeepClone();

                    if (!container.ContainsKey("elementId"))
                    {
                        var e = doc.GetElement(suidTok.Value<string>());
                        if (e != null) container["elementId"] = e.Id.IntValue();
                    }
                }
            }
        }

        private static Element? TryGetElementFromContainer(Document doc, JObject? obj)
        {
            if (obj == null) return null;

            // 既存キー
            if (obj.TryGetValue("uniqueId", out var uidTok) && uidTok.Type == JTokenType.String)
            {
                var e = doc.GetElement(uidTok.Value<string>());
                if (e != null) return e;
            }
            if (obj.TryGetValue("elementId", out var eidTok) && eidTok.Type == JTokenType.Integer)
            {
                var e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eidTok.Value<int>()));
                if (e != null) return e;
            }

            // ★ 追加: 別名キー（viewId/typeId/...）からも代表要素を解決
            var altIdKeys = new[]
            {
                "viewId", "typeId", "newTypeId",
                "scheduleViewId", "sheetId", "revisionId",
                "gridId", "levelId", "materialId"
            };
            for (int i = 0; i < altIdKeys.Length; i++)
            {
                var idKey = altIdKeys[i];
                if (obj.TryGetValue(idKey, out var sidTok) && sidTok.Type == JTokenType.Integer)
                {
                    var e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(sidTok.Value<int>()));
                    if (e != null) return e;
                }
                var uidKey = idKey.Replace("Id", "UniqueId");
                if (obj.TryGetValue(uidKey, out var suidTok) && suidTok.Type == JTokenType.String)
                {
                    var e = doc.GetElement(suidTok.Value<string>());
                    if (e != null) return e;
                }
            }

            return null;
        }
    }
}


