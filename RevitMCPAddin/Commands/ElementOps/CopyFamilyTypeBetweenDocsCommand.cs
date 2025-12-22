// ================================================================
// File: Commands/ElementOps/CopyFamilyTypeBetweenDocsCommand.cs  (UnitHelper対応: 参照のみ)
// Revit 2023+ / .NET Framework 4.8
// 概要:
//  - copy_family_type_between_docs
//    同一Revit起動内の別ドキュメント間で「タイプ（ElementType）」をコピー
//    ・ロード可能ファミリ（FamilySymbol）/ システムファミリ（WallType 等）対応
//    ・指定方法: id / uniqueId / nameTriplet(カテゴリ+ファミリ+タイプ) / familyAllTypes
//    ・重名ポリシー: useDestination | abort
//    ・dryRun対応
// 返却: { ok, copiedCount, items:[{source{...}, target{...}, action, msg?}], issues{...} }
// 備考:
//  - 本コマンド自体は幾何の単位変換を伴わないため、UnitHelper 依存の呼び出しはありません。
//    将来の一貫性のため、Core 名前空間参照のみ追加しています。
// ================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core; // UnitHelper / ResultUtil 等（本ファイルでは直接未使用）

namespace RevitMCPAddin.Commands.ElementOps
{
    public class CopyFamilyTypeBetweenDocsCommand : IRevitCommandHandler
    {
        public string CommandName => "copy_family_type_between_docs";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var app = uiapp.Application;

            var p = cmd.Params as JObject ?? new JObject();
            var sourceSpec = (JObject)p["source"];
            var targetSpec = (JObject)p["target"]; // nullならActive
            var requests = (JArray)(p["requests"] ?? new JArray());
            var conflictPolicy = (string)(p["conflictPolicy"] ?? "useDestination");
            var dryRun = (bool?)p["dryRun"] ?? false;

            var result = new JObject
            {
                ["ok"] = false,
                ["items"] = new JArray(),
                ["issues"] = new JObject
                {
                    ["failures"] = new JArray(),
                    ["dialogs"] = new JArray()
                }
            };
            var items = (JArray)result["items"];

            // --- Resolve documents ---
            var srcDoc = ResolveDocument(app, sourceSpec, preferActive: false);
            var dstDoc = ResolveDocument(app, targetSpec, preferActive: true);

            if (srcDoc == null)
                return Fail(result, "source document not found/closed.");
            if (dstDoc == null)
                return Fail(result, "target document not found/closed.");
            if (srcDoc.Equals(dstDoc))
            {
                // 同Doc内コピーは TransferProjectStandards 相当の意味が薄いので許容しない
                return Fail(result, "source and target documents are the same.");
            }

            // --- Build copy list from selectors ---
            var toCopySrcTypeIds = new List<ElementId>();
            var perRequestPlans = new List<PerRequestPlan>();

            foreach (var reqTok in requests)
            {
                var req = (JObject)reqTok;
                var sel = (JObject)req["selector"];
                if (sel == null) { items.Add(MakeItem(null, null, "failed", "selector missing")); continue; }

                var by = (string)sel["by"];
                try
                {
                    switch (by)
                    {
                        case "id":
                            {
                                var id = (int)sel["elementId"];
                                var et = srcDoc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id)) as ElementType;
                                if (et == null) { items.Add(MakeItem(null, null, "failed", $"ElementId {id} is not ElementType in source.")); }
                                else
                                {
                                    toCopySrcTypeIds.Add(et.Id);
                                    perRequestPlans.Add(new PerRequestPlan(et, req));
                                }
                                break;
                            }
                        case "uniqueId":
                            {
                                var uid = (string)sel["uniqueId"];
                                var et = srcDoc.GetElement(uid) as ElementType;
                                if (et == null) { items.Add(MakeItem(null, null, "failed", $"UniqueId not found or not ElementType: {uid}")); }
                                else
                                {
                                    toCopySrcTypeIds.Add(et.Id);
                                    perRequestPlans.Add(new PerRequestPlan(et, req));
                                }
                                break;
                            }
                        case "nameTriplet":
                            {
                                var catStr = (string)sel["category"];
                                var fam = (string)sel["familyName"];
                                var typ = (string)sel["typeName"];
                                var ids = FindTypesByNameTriplet(srcDoc, catStr, fam, typ);
                                if (ids.Count == 0) { items.Add(MakeItem(null, null, "failed", $"No match: {catStr} / {fam} / {typ}")); }
                                else
                                {
                                    foreach (var id in ids)
                                    {
                                        var et = (ElementType)srcDoc.GetElement(id);
                                        toCopySrcTypeIds.Add(id);
                                        perRequestPlans.Add(new PerRequestPlan(et, req));
                                    }
                                }
                                break;
                            }
                        case "familyAllTypes":
                            {
                                var catStr = (string)sel["category"];
                                var fam = (string)sel["familyName"];
                                var ids = FindFamilyAllTypes(srcDoc, catStr, fam);
                                if (ids.Count == 0) { items.Add(MakeItem(null, null, "failed", $"No types in family: {catStr} / {fam}")); }
                                else
                                {
                                    foreach (var id in ids)
                                    {
                                        var et = (ElementType)srcDoc.GetElement(id);
                                        toCopySrcTypeIds.Add(id);
                                        perRequestPlans.Add(new PerRequestPlan(et, req));
                                    }
                                }
                                break;
                            }
                        default:
                            items.Add(MakeItem(null, null, "failed", $"Unsupported selector.by: {by}"));
                            break;
                    }
                }
                catch (Exception ex)
                {
                    items.Add(MakeItem(null, null, "failed", $"selector error: {ex.Message}"));
                }
            }

            // 何も対象がなければ終了
            if (toCopySrcTypeIds.Count == 0)
            {
                result["ok"] = false;
                result["msg"] = "No ElementTypes found to copy.";
                return result;
            }

            // dry-run: 対象一覧のみ返却
            if (dryRun)
            {
                foreach (var plan in perRequestPlans)
                    items.Add(MakeItem(DescribeType(srcDoc, plan.SourceType), null, "plan", null));
                result["ok"] = true;
                result["copiedCount"] = 0;
                result["msg"] = "Dry-run: no changes made.";
                return result;
            }

            // --- Execute copy ---
            var handler = new DuplicateTypeNamesHandler(conflictPolicy);
            var cpo = new CopyPasteOptions();
            cpo.SetDuplicateTypeNamesHandler(handler);

            var copiedCount = 0;
            using (var t = new Transaction(dstDoc, "[MCP] Copy Family Types"))
            {
                t.Start();
                try
                {
                    // ElementType は座標不要 → Transform.Identity
                    var newIds = ElementTransformUtils.CopyElements(
                        srcDoc,
                        toCopySrcTypeIds,
                        dstDoc,
                        Transform.Identity,
                        cpo
                    );

                    // CopyElements の返り順は入力順に対応
                    for (int i = 0; i < toCopySrcTypeIds.Count; i++)
                    {
                        var srcId = toCopySrcTypeIds[i];
                        var srcType = (ElementType)srcDoc.GetElement(srcId);
                        var descSrc = DescribeType(srcDoc, srcType);

                        // 返ってこないケース（useDestination のためコピーされなかった 等）
                        var newId = (i < newIds.Count) ? newIds.ElementAt(i) : ElementId.InvalidElementId;
                        if (newId != ElementId.InvalidElementId)
                        {
                            // 新規要素ができた（複製 or リネームなど）
                            var dstEl = dstDoc.GetElement(newId) as ElementType;
                            items.Add(MakeItem(descSrc, DescribeType(dstDoc, dstEl), handler.LastActionForRun ?? "copied", null));
                            if (dstEl != null) copiedCount++;
                        }
                        else
                        {
                            // 同名が既存→useDestination の場合はコピーしない（exists扱い）
                            // abort はトランザクション全体が例外になるためここには来ない
                            items.Add(MakeItem(descSrc, null, "exists", "Skipped: destination has a type with the same name."));
                        }
                    }

                    t.Commit();
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    return Fail(result, $"Copy failed: {ex.Message}");
                }
            }

            result["ok"] = true;
            result["copiedCount"] = copiedCount;
            result["msg"] = $"Done. copied={copiedCount}, totalRequested={toCopySrcTypeIds.Count}";
            return result;
        }

        // -------- Helpers --------

        private static Document ResolveDocument(Autodesk.Revit.ApplicationServices.Application app, JObject spec, bool preferActive)
        {
            if (spec == null && preferActive)
            {
                // ActiveUIDocument がないケース（例えばリンクドモデルのみ）はあり得るため防御
                var uiapp = new UIApplication(app);
                return uiapp.ActiveUIDocument?.Document;
            }
            if (spec == null) return null;

            // docTitle / docPath / docId 優先順で解決
            var title = (string)spec["docTitle"];
            var path = (string)spec["docPath"];
            var id = (int?)spec["docId"];

            foreach (Document d in app.Documents)
            {
                if (!d.IsValidObject) continue;
                if (id.HasValue && d.GetHashCode() == id.Value) return d; // 簡易Id（必要なら拡張可）
                if (!string.IsNullOrEmpty(path) && string.Equals(d.PathName, path, StringComparison.OrdinalIgnoreCase)) return d;
                if (!string.IsNullOrEmpty(title) && string.Equals(d.Title, title, StringComparison.OrdinalIgnoreCase)) return d;
            }
            return null;
        }

        private class PerRequestPlan
        {
            public ElementType SourceType { get; }
            public JObject OriginalRequest { get; }
            public PerRequestPlan(ElementType et, JObject req)
            {
                SourceType = et; OriginalRequest = req;
            }
        }

        private static IList<ElementId> FindTypesByNameTriplet(Document doc, string categoryNameOrId, string familyName, string typeName)
        {
            var all = new FilteredElementCollector(doc).OfClass(typeof(ElementType)).Cast<ElementType>();
            var filtered = all.Where(et => CategoryMatch(et.Category, categoryNameOrId));
            if (!string.IsNullOrEmpty(familyName))
                filtered = filtered.Where(et => SafeEquals(et.FamilyName, familyName));
            if (!string.IsNullOrEmpty(typeName))
                filtered = filtered.Where(et => SafeEquals(et.Name, typeName));
            return filtered.Select(et => et.Id).ToList();
        }

        private static IList<ElementId> FindFamilyAllTypes(Document doc, string categoryNameOrId, string familyName)
        {
            var all = new FilteredElementCollector(doc).OfClass(typeof(ElementType)).Cast<ElementType>();
            var filtered = all.Where(et => CategoryMatch(et.Category, categoryNameOrId))
                              .Where(et => SafeEquals(et.FamilyName, familyName));
            return filtered.Select(et => et.Id).ToList();
        }

        private static bool SafeEquals(string a, string b)
            => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);

        private static bool CategoryMatch(Category cat, string nameOrId)
        {
            if (cat == null) return false;
            if (string.IsNullOrEmpty(nameOrId)) return true;

            // 数値指定（BuiltInCategoryの整数IDなど）にも緩く対応
            if (int.TryParse(nameOrId, out var idInt))
                return cat.Id.IntValue() == idInt;

            return string.Equals(cat.Name, nameOrId, StringComparison.OrdinalIgnoreCase);
        }

        private static JObject DescribeType(Document doc, ElementType et)
        {
            if (et == null) return null;
            return new JObject
            {
                ["doc"] = doc?.Title,
                ["id"] = et.Id?.IntValue(),
                ["uniqueId"] = et.UniqueId,
                ["name"] = et.Name,
                ["family"] = et.FamilyName,
                ["category"] = et.Category?.Name,
                ["categoryId"] = et.Category?.Id?.IntValue()
            };
        }

        private static JObject MakeItem(JObject src, JObject dst, string action, string msg)
        {
            var o = new JObject
            {
                ["source"] = src,
                ["target"] = dst,
                ["action"] = action
            };
            if (!string.IsNullOrEmpty(msg)) o["msg"] = msg;
            return o;
        }

        private static JObject Fail(JObject result, string msg)
        {
            result["ok"] = false;
            result["msg"] = msg;
            result["error"] = new JObject
            {
                ["humanMessage"] = msg
            };
            return result;
        }

        // 衝突時の動作
        private class DuplicateTypeNamesHandler : IDuplicateTypeNamesHandler
        {
            private readonly string _policy;
            public string LastActionForRun { get; private set; } // "copied" | "exists" | "failed"
            public DuplicateTypeNamesHandler(string policy)
            {
                _policy = (policy ?? "useDestination").ToLowerInvariant();
            }

            public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
            {
                switch (_policy)
                {
                    case "abort":
                        LastActionForRun = "failed";
                        return DuplicateTypeAction.Abort;

                    case "usedestination":
                    default:
                        LastActionForRun = "exists";
                        return DuplicateTypeAction.UseDestinationTypes;
                }
            }
        }
    }
}



