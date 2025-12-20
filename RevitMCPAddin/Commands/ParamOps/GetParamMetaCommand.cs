#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core; // ResultUtil, RequestCommand

namespace RevitMCPAddin.Commands.ParamOps
{
    /// <summary>
    /// JSON-RPC: get_param_meta
    /// 値は読まず、パラメータのメタ情報（名前/ID/StorageType/DataType/種別/共有/discipline推定）だけ返す軽量コマンド
    /// </summary>
    public class GetParamMetaCommand : IRevitCommandHandler
    {
        public string CommandName => "get_param_meta";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            var p = (JObject)cmd.Params;

            // ---- 入力
            var target = p["target"] as JObject ?? new JObject();
            var by = (target.Value<string>("by") ?? "").Trim().ToLowerInvariant();
            var valueToken = target["value"];

            var include = p["include"] as JObject;
            bool includeInstance = include?.Value<bool?>("instance") ?? true;
            bool includeType = include?.Value<bool?>("type") ?? true;

            var filterNames = p["filterNames"]?.ToObject<List<string>>() ?? new List<string>();
            var maxCount = p.Value<int?>("maxCount") ?? 0;

            if (string.IsNullOrEmpty(by))
                return ResultUtil.Err("target.by が指定されていません。 (elementId|typeId|uniqueId|roomId)");

            // ---- ターゲット解決
            Element targetElem = null;
            Element targetTypeElem = null;
            string targetKind = "unknown";
            ElementId canonicalElementId = null; // 参照型なので null 使用

            try
            {
                switch (by)
                {
                    case "elementid":
                        {
                            int id = valueToken?.Value<int>() ?? -1;
                            if (id <= 0) return ResultUtil.Err("target.value に正の elementId (int) を指定して下さい。");
                            targetElem = doc.GetElement(new ElementId(id));
                            if (targetElem == null) return ResultUtil.Err($"要素が見つかりません: elementId={id}");
                            canonicalElementId = targetElem.Id;
                            targetKind = "element";
                            break;
                        }
                    case "typeid":
                        {
                            int id = valueToken?.Value<int>() ?? -1;
                            if (id <= 0) return ResultUtil.Err("target.value に正の typeId (int) を指定して下さい。");
                            targetTypeElem = doc.GetElement(new ElementId(id));
                            if (targetTypeElem == null) return ResultUtil.Err($"タイプ要素が見つかりません: typeId={id}");
                            canonicalElementId = targetTypeElem.Id;
                            targetKind = "type";
                            break;
                        }
                    case "uniqueid":
                        {
                            string uid = valueToken?.Value<string>() ?? "";
                            if (string.IsNullOrWhiteSpace(uid)) return ResultUtil.Err("target.value に uniqueId (string) を指定して下さい。");
                            targetElem = doc.GetElement(uid);
                            if (targetElem == null) return ResultUtil.Err($"要素が見つかりません: uniqueId={uid}");
                            canonicalElementId = targetElem.Id;
                            targetKind = "element";
                            break;
                        }
                    case "roomid":
                        {
                            int id = valueToken?.Value<int>() ?? -1;
                            if (id <= 0) return ResultUtil.Err("target.value に正の roomId (int) を指定して下さい。");
                            var e = doc.GetElement(new ElementId(id));
                            if (e == null) return ResultUtil.Err($"部屋が見つかりません: roomId={id}");
                            targetElem = e; // Room も Element
                            canonicalElementId = e.Id;
                            targetKind = "room";
                            break;
                        }
                    default:
                        return ResultUtil.Err($"未対応の target.by: {by}");
                }
            }
            catch (Exception ex)
            {
                return ResultUtil.Err($"ターゲット解決中に例外: {ex.Message}");
            }

            // ---- 列挙
            var results = new List<object>();
            // v2: optional SPF join
            SpfCatalog spfCatalog = null;
            Dictionary<Guid, SharedParameterElement> sharedParamByGuid = null;
            try
            {
                bool joinSpf = p.Value<bool?>("joinSharedParameterFile") ?? false;
                if (joinSpf && SpfCatalog.TryLoad(p["spf"], out var cat, out var _))
                    spfCatalog = cat;
            }
            catch { }
            int totalCount = 0;

            if (includeInstance && targetElem != null)
            {
                AppendParams(results, targetElem, isType: false, filterNames, ref totalCount, maxCount, spfCatalog, ref sharedParamByGuid);
            }

            if (includeType)
            {
                    if (targetTypeElem != null)
                    {
                        AppendParams(results, targetTypeElem, isType: true, filterNames, ref totalCount, maxCount, spfCatalog, ref sharedParamByGuid);
                    }
                else if (targetElem != null)
                {
                    var typeId = targetElem.GetTypeId();
                    if (typeId != ElementId.InvalidElementId)
                    {
                        var te = doc.GetElement(typeId);
                        if (te != null) AppendParams(results, te, isType: true, filterNames, ref totalCount, maxCount, spfCatalog, ref sharedParamByGuid);
                    }
                }
            }

            // ---- 返却
            var tgtObj = new JObject { ["kind"] = targetKind };
            if (canonicalElementId != null) tgtObj["id"] = canonicalElementId.IntegerValue;
            if (targetElem != null) tgtObj["uniqueId"] = targetElem.UniqueId;

            return ResultUtil.Ok(new
            {
                target = tgtObj,
                parameters = results,
                counts = new { total = totalCount, returned = results.Count }
            });
        }

        // ================= ヘルパ =================

        private static void AppendParams(
            List<object> sink,
            Element e,
            bool isType,
            List<string> filterNames,
            ref int totalCount,
            int maxCount,
            SpfCatalog spfCatalog,
            ref Dictionary<Guid, SharedParameterElement> sharedParamByGuid
        )
        {
            var filter = BuildNameFilter(filterNames);

            foreach (Parameter prm in e.Parameters)
            {
                totalCount++;

                string name = SafeParamName(prm);
                if (filter != null && !filter(name)) continue;

                if (maxCount > 0 && sink.Count >= maxCount) break;

                string storage = prm.StorageType.ToString();

                // DataType(ForgeTypeId) 取得：反射のみで安全に。無ければ null
                ForgeTypeId spec = TryGetDataTypeId(prm);
                string dataTypeId = spec != null ? spec.TypeId : "";
                string dataTypeLabel = spec != null ? SafeLabelForSpec(spec) : "";

                string discipline = InferDiscipline(dataTypeId, prm);

                // v2 enrich: origin, projectGroup, guid canonical, spf join
                string origin = null;
                int pid = prm.Id?.IntegerValue ?? 0;
                bool isBuiltIn = pid < 0;
                bool isShared = SafeIsShared(prm);
                origin = isBuiltIn ? "builtIn" : (isShared ? "shared" : "project");

                string projectGroupEnum = null, projectGroupUi = null;
                try
                {
                    var grp = prm.Definition?.ParameterGroup ?? BuiltInParameterGroup.INVALID;
                    projectGroupEnum = grp.ToString();
                    try { projectGroupUi = LabelUtils.GetLabelFor(grp); } catch { projectGroupUi = null; }
                }
                catch { }

                string guid = null;
                try
                {
                    var def = prm.Definition;
                    var ext = def as ExternalDefinition;
                    Guid? extGuid = null; if (ext != null && ext.GUID != Guid.Empty) extGuid = ext.GUID;
                    // prefer SPE.GuidValue if found by GUID
                    if (extGuid.HasValue)
                    {
                        if (sharedParamByGuid == null)
                        {
                            sharedParamByGuid = BuildSharedParameterGuidIndex(e.Document);
                        }

                        SharedParameterElement spe = null;
                        if (sharedParamByGuid != null)
                        {
                            sharedParamByGuid.TryGetValue(extGuid.Value, out spe);
                        }

                        if (spe != null && spe.GuidValue != Guid.Empty)
                            guid = spe.GuidValue.ToString();
                        else
                            guid = extGuid.Value.ToString();
                    }
                }
                catch { }

                string spfGroup = null; object spfMeta = null;
                try
                {
                    if (spfCatalog != null && !string.IsNullOrWhiteSpace(guid))
                    {
                        if (spfCatalog.TryGetByGuid(guid, out var meta))
                        {
                            spfGroup = meta.GroupName;
                            spfMeta = new
                            {
                                source = "sharedParameterFile",
                                groupId = meta.GroupId,
                                groupName = meta.GroupName,
                                dataType = meta.DataType,
                                extra = meta.Extra.Count > 0 ? (object)meta.Extra : null
                            };
                        }
                    }
                }
                catch { }

                sink.Add(new
                {
                    name,
                    id = pid,
                    storageType = storage,
                    dataType = new { id = dataTypeId, label = dataTypeLabel },
                    kind = isType ? "type" : "instance",
                    isShared,
                    discipline,
                    origin,
                    projectGroup = new { @enum = projectGroupEnum, uiLabel = projectGroupUi },
                    guid,
                    spfGroup,
                    spfMeta
                });
            }
        }

        private static Dictionary<Guid, SharedParameterElement> BuildSharedParameterGuidIndex(Document doc)
        {
            if (doc == null) return null;

            try
            {
                var dict = new Dictionary<Guid, SharedParameterElement>();
                var coll = new FilteredElementCollector(doc)
                    .OfClass(typeof(SharedParameterElement))
                    .Cast<SharedParameterElement>();

                foreach (var spe in coll)
                {
                    if (spe == null) continue;
                    Guid g;
                    try { g = spe.GuidValue; } catch { continue; }
                    if (g == Guid.Empty) continue;
                    if (!dict.ContainsKey(g)) dict.Add(g, spe);
                }

                return dict;
            }
            catch
            {
                return null;
            }
        }

        private static Func<string, bool> BuildNameFilter(List<string> names)
        {
            if (names == null || names.Count == 0) return null;
            var set = new HashSet<string>(names.Where(s => !string.IsNullOrWhiteSpace(s)),
                                          StringComparer.OrdinalIgnoreCase);
            return (n) => set.Contains(n ?? "");
        }

        private static string SafeParamName(Parameter prm)
        {
            try { return prm.Definition?.Name ?? prm?.ToString() ?? ""; }
            catch { return prm?.ToString() ?? ""; }
        }

        private static bool SafeIsShared(Parameter prm)
        {
            try { return prm.IsShared; } catch { return false; }
        }

        /// <summary>
        /// GetDataTypeId が無い環境でもビルド可能にするため、反射のみで試みる。
        /// 取得できなければ null（=空扱い）
        /// </summary>
        private static ForgeTypeId TryGetDataTypeId(Parameter prm)
        {
            try
            {
                var def = prm.Definition;
                if (def == null) return null;

                var mi = def.GetType().GetMethod("GetDataTypeId", Type.EmptyTypes);
                if (mi != null)
                {
                    var v = mi.Invoke(def, null) as ForgeTypeId;
                    return v;
                }
            }
            catch { /* nop */ }
            return null;
        }

        private static string SafeLabelForSpec(ForgeTypeId spec)
        {
            try
            {
                var mi = typeof(LabelUtils).GetMethod("GetLabelForSpec", new[] { typeof(ForgeTypeId) });
                if (mi != null)
                {
                    var s = mi.Invoke(null, new object[] { spec }) as string;
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
            catch { /* nop */ }
            return spec?.TypeId ?? "";
        }

        private static string InferDiscipline(string dataTypeId, Parameter prm)
        {
            var key = (dataTypeId ?? "").ToLowerInvariant();

            if (key.Contains(":electrical")) return "Electrical";
            if (key.Contains(":hvac") || key.Contains(":mechanical")) return "Mechanical";
            if (key.Contains(":piping")) return "Piping";
            if (key.Contains(":structural")) return "Structural";
            if (key.Contains(":plumbing")) return "Plumbing";
            if (key.Contains(":architecture")) return "Architecture";

            // 共有パラメータの ParameterGroup から推測
            try
            {
                var def = prm.Definition;
                if (def is ExternalDefinition ext)
                {
                    var grp = ext.ParameterGroup.ToString().ToUpperInvariant();
                    if (grp.Contains("MECHANICAL")) return "Mechanical";
                    if (grp.Contains("ELECTRICAL")) return "Electrical";
                    if (grp.Contains("PLUMBING")) return "Plumbing";
                    if (grp.Contains("STRUCTURAL")) return "Structural";
                    if (grp.Contains("ARCHITECT")) return "Architecture";
                }
            }
            catch { /* nop */ }

            return "Common";
        }
    }
}
