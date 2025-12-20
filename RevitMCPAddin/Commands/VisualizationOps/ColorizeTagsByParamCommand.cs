// ================================================================
// File: Commands/VisualizationOps/ColorizeTagsByParamCommand.cs
// Purpose: Colorize annotation tags in the active view (or selection)
//          based on a string parameter and mapping table.
// Scope  : Active view (or selection if any elements are selected)
// Notes  : Based on Design/Revit_TagColorizer_Addin_DesignDoc.md
//          Supports tag parameters and (optionally) host element parameters.
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.VisualizationOps
{
    public class ColorizeTagsByParamCommand : IRevitCommandHandler
    {
        public string CommandName => "colorize_tags_by_param";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            var view = uidoc?.ActiveView as View;
            if (doc == null || view == null)
                return new { ok = false, msg = "アクティブビューまたはドキュメントが見つかりません。" };

            var p = (JObject?)(cmd.Params ?? new JObject()) ?? new JObject();

            // Config section may be nested under "config" or flattened at top level.
            var cfg = p["config"] as JObject ?? p;

            string paramName = (cfg.Value<string>("parameterName") ?? "Comments").Trim();
            if (string.IsNullOrWhiteSpace(paramName))
                paramName = "Comments";

            // When true, try host element parameters first, then fall back to tag.
            bool readFromHost = cfg.Value<bool?>("readFromHost") ?? false;

            // If a view template is applied, skip colorization and instruct caller to detach.
            if (view.ViewTemplateId != ElementId.InvalidElementId)
            {
                return new
                {
                    ok = true,
                    viewId = view.Id.IntegerValue,
                    total = 0,
                    changed = 0,
                    skippedNoParam = 0,
                    skippedNoColor = 0,
                    parameterName = paramName,
                    templateApplied = true,
                    templateViewId = view.ViewTemplateId.IntegerValue,
                    skippedDueToTemplate = true,
                    errorCode = "VIEW_TEMPLATE_LOCK",
                    message = "View has a template; detach view template before calling colorize_tags_by_param."
                };
            }

            // Target categories by BuiltInCategory name (e.g. "OST_RoomTags").
            var targetCatIds = BuildTargetCategoryIds(doc, cfg);
            if (targetCatIds.Count == 0)
            {
                return new
                {
                    ok = true,
                    viewId = view.Id.IntegerValue,
                    total = 0,
                    changed = 0,
                    skippedNoParam = 0,
                    skippedNoColor = 0,
                    parameterName = paramName,
                    message = "No valid targetCategories configured; nothing to do."
                };
            }

            var elements = GetTargetElements(doc, uidoc, view, targetCatIds);
            if (elements.Count == 0)
            {
                return new
                {
                    ok = true,
                    viewId = view.Id.IntegerValue,
                    total = 0,
                    changed = 0,
                    skippedNoParam = 0,
                    skippedNoColor = 0,
                    parameterName = paramName,
                    message = "No tag elements found in active view (or selection)."
                };
            }

            var mappings = BuildMappings(cfg);
            var defaultColorOpt = TryParseColor(cfg["defaultColor"]);

            int changedCount = 0;
            int skippedNoParam = 0;
            int skippedNoColor = 0;

            using (var tx = new Transaction(doc, "[MCP] Colorize Tags by Parameter"))
            {
                tx.Start();

                foreach (var el in elements)
                {
                    string value = GetParamValue(doc, el, paramName, readFromHost);

                    Color? colorOpt;
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        // パラメータが無い/空でも defaultColor があればそれを適用する。
                        if (defaultColorOpt != null)
                        {
                            colorOpt = defaultColorOpt;
                        }
                        else
                        {
                            skippedNoParam++;
                            continue;
                        }
                    }
                    else
                    {
                        colorOpt = FindColorForValue(value, mappings, defaultColorOpt);
                        if (colorOpt == null)
                        {
                            skippedNoColor++;
                            continue;
                        }
                    }

                    var ogs = view.GetElementOverrides(el.Id);
                    ogs.SetProjectionLineColor(colorOpt);
                    ogs.SetCutLineColor(colorOpt);
                    view.SetElementOverrides(el.Id, ogs);
                    changedCount++;
                }

                tx.Commit();
            }

            try
            {
                doc.Regenerate();
                uidoc?.RefreshActiveView();
            }
            catch
            {
                // best-effort; ignore
            }

            return new
            {
                ok = true,
                viewId = view.Id.IntegerValue,
                total = elements.Count,
                changed = changedCount,
                skippedNoParam,
                skippedNoColor,
                parameterName = paramName,
                categories = targetCatIds.ToArray()
            };
        }

        private static HashSet<int> BuildTargetCategoryIds(Document doc, JObject cfg)
        {
            var ids = new HashSet<int>();

            var catNamesArr = cfg["targetCategories"] as JArray;
            List<string> names;
            if (catNamesArr != null)
            {
                names = catNamesArr
                    .Values<string>()
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
            }
            else
            {
                // Defaults: Room/Door/Window tags and generic annotation
                names = new List<string>
                {
                    "OST_RoomTags",
                    "OST_DoorTags",
                    "OST_WindowTags",
                    "OST_GenericAnnotation"
                };
            }

            foreach (var name in names)
            {
                try
                {
                    if (Enum.TryParse(name.Trim(), ignoreCase: true, out BuiltInCategory bic))
                    {
                        var cat = Category.GetCategory(doc, bic);
                        if (cat != null)
                        {
                            ids.Add(cat.Id.IntegerValue);
                        }
                    }
                }
                catch
                {
                    // ignore invalid category names
                }
            }

            return ids;
        }

        private static List<Element> GetTargetElements(Document doc, UIDocument? uidoc, View view, HashSet<int> targetCatIds)
        {
            var result = new List<Element>();
            var selIds = uidoc?.Selection?.GetElementIds() ?? new List<ElementId>();

            // If there is an explicit selection, honor it as-is (do not filter by category).
            if (selIds != null && selIds.Count > 0)
            {
                foreach (var id in selIds)
                {
                    var el = doc.GetElement(id);
                    if (el != null)
                    {
                        result.Add(el);
                    }
                }
                return result;
            }

            // No selection: filter by target tag categories in the active view.
            var collector = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType();

            foreach (var el in collector)
            {
                var cat = el.Category;
                if (cat == null) continue;
                if (targetCatIds.Contains(cat.Id.IntegerValue))
                {
                    result.Add(el);
                }
            }

            return result;
        }

        private static string Normalize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            return text.Trim().ToUpperInvariant();
        }

        private static List<(string keyNorm, Color color)> BuildMappings(JObject cfg)
        {
            var list = new List<(string, Color)>();
            if (!(cfg["mappings"] is JObject mapObj)) return list;

            foreach (var prop in mapObj.Properties())
            {
                var key = Normalize(prop.Name);
                if (string.IsNullOrEmpty(key)) continue;

                var colorTok = prop.Value;
                var colOpt = TryParseColor(colorTok);
                if (colOpt != null)
                {
                    list.Add((key, colOpt));
                }
            }

            return list;
        }

        private static Color? TryParseColor(JToken? token)
        {
            if (token == null) return null;

            try
            {
                if (token is JArray arr && arr.Count >= 3)
                {
                    byte r = (byte)arr[0].Value<int>();
                    byte g = (byte)arr[1].Value<int>();
                    byte b = (byte)arr[2].Value<int>();
                    return new Color(r, g, b);
                }
                if (token is JObject obj)
                {
                    byte r = (byte)(obj.Value<int?>("r") ?? 0);
                    byte g = (byte)(obj.Value<int?>("g") ?? 0);
                    byte b = (byte)(obj.Value<int?>("b") ?? 0);
                    return new Color(r, g, b);
                }
            }
            catch
            {
                // ignore parse errors
            }

            return null;
        }

        private static Color? FindColorForValue(string value, List<(string keyNorm, Color color)> mappings, Color? defaultColor)
        {
            var valueNorm = Normalize(value);
            if (string.IsNullOrEmpty(valueNorm))
                return null;

            foreach (var (keyNorm, color) in mappings)
            {
                if (string.IsNullOrEmpty(keyNorm)) continue;
                if (valueNorm.Contains(keyNorm))
                    return color;
            }

            return defaultColor;
        }

        private static Element? TryGetHostElement(Document doc, Element el)
        {
            Element? host = null;

            // 1) IndependentTag ベースの一般的なタグ（ドアタグ等）
            if (el is IndependentTag indep)
            {
                try
                {
                    var hostIds = indep.GetTaggedLocalElementIds();
                    if (hostIds != null && hostIds.Count > 0)
                    {
                        host = doc.GetElement(hostIds.First());
                        if (host != null)
                            return host;
                    }
                }
                catch
                {
                    // ignore and fall back to other strategies
                }
            }

            // 2) 部屋タグ（RoomTag）は IndependentTag とは別系統なので RoomTag.Room から取得
            if (host == null && el is RoomTag roomTag)
            {
                try
                {
                    var room = roomTag.Room;
                    if (room != null)
                        host = room;
                }
                catch
                {
                    // ignore and fall back to tag-only
                }
            }

            return host;
        }

        private static string GetParamValue(Document doc, Element el, string paramName, bool readFromHost)
        {
            if (string.IsNullOrWhiteSpace(paramName)) return string.Empty;

            Parameter? ResolveParam(Element? src)
            {
                if (src == null) return null;
                try { return src.LookupParameter(paramName); }
                catch { return null; }
            }

            Parameter? param;
            if (readFromHost)
            {
                var host = TryGetHostElement(doc, el);
                param = ResolveParam(host) ?? ResolveParam(el);
            }
            else
            {
                param = ResolveParam(el);
            }

            if (param == null) return string.Empty;

            try
            {
                if (param.StorageType == StorageType.String)
                {
                    return param.AsString() ?? string.Empty;
                }

                var vs = param.AsValueString();
                return vs ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
