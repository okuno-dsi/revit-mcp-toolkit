#nullable enable
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System.Linq;

namespace RevitMCPAddin.Commands.ViewOps
{
    /// <summary>
    /// Duplicate the current/target view with a robust parameter set.
    /// - params:
    ///   - viewId?: int            // target view element id; if omitted, use active view
    ///   - uniqueId?: string       // target view unique id; overrides viewId if provided
    ///   - withDetailing?: bool = true   // try WithDetailing first
    ///   - namePrefix?: string           // legacy optional name prefix (kept for compatibility)
    ///   - desiredName?: string          // strict desired name for the new view (preferred)
    ///   - onNameConflict?: string       // 'returnExisting' | 'increment' | 'timestamp' | 'fail' (default: 'increment')
    ///   - idempotencyKey?: string       // if provided, return previously created view for the same key (process-local)
    /// - returns (success):
    ///   { ok: true, viewId, elementId, uniqueId, name, created: bool, conflict?: string }
    /// - returns (failure):
    ///   { ok: false, code?: string, msg: string }
    /// </summary>
    public class DuplicateViewSimpleCommand : IRevitCommandHandler
    {
        public string CommandName => "duplicate_view";

        // 返却用のDTO（匿名型だとシリアライザ設定で欠落することがあるため明示クラス化）
        public class DuplicateViewResultDto
        {
            public bool ok { get; set; }
            public int viewId { get; set; }        // camelCase固定
            public int elementId { get; set; }     // 互換のために別名も返す（= viewId と同値）
            public string uniqueId { get; set; } = string.Empty;
            public string name { get; set; } = string.Empty;
            public bool created { get; set; }
            public string? conflict { get; set; }
        }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var uidoc = uiapp?.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc == null)
                    return new { ok = false, msg = "No active document." };

                // 入力パラメータ
                var p = cmd.Params as JObject ?? new JObject();
                // Optional execution guard
                var guard = RevitMCPAddin.Core.ExpectedContextGuard.Validate(uiapp, p);
                if (guard != null) return guard;
                bool withDetailing = p.Value<bool?>("withDetailing") ?? true;
                string? namePrefix = p.Value<string>("namePrefix");
                string? desiredName = p.Value<string>("desiredName");
                string onNameConflict = (p.Value<string>("onNameConflict") ?? "increment").Trim().ToLowerInvariant();
                switch (onNameConflict)
                {
                    case "returnexisting":
                    case "increment":
                    case "timestamp":
                    case "fail":
                        break;
                    default:
                        onNameConflict = "increment";
                        break;
                }
                string? idemKey = p.Value<string>("idempotencyKey");

                // 対象ビューの解決（uniqueId > viewId > ActiveView）
                View? baseView = null;

                string? uniqueId = p.Value<string>("uniqueId");
                if (!string.IsNullOrEmpty(uniqueId))
                {
                    var e = doc.GetElement(uniqueId);
                    baseView = e as View;
                }
                else
                {
                    int? viewIdInt = p.Value<int?>("viewId");
                    if (viewIdInt.HasValue && viewIdInt.Value > 0)
                    {
                        var e = doc.GetElement(new ElementId(viewIdInt.Value));
                        baseView = e as View;
                    }
                    else
                    {
                        baseView = uidoc?.ActiveView;
                    }
                }

                if (baseView == null)
                    return new { ok = false, msg = "Target view could not be resolved." };

                // Idempotency: if key given and seen before, return that view (if still exists)
                if (!string.IsNullOrWhiteSpace(idemKey) && Core.IdempotencyRegistry.TryGet(idemKey!, out var storedUid) && !string.IsNullOrWhiteSpace(storedUid))
                {
                    var storedElem = doc.GetElement(storedUid);
                    var storedView = storedElem as View;
                    if (storedView != null && !storedView.IsTemplate)
                    {
                        return new DuplicateViewResultDto
                        {
                            ok = true,
                            created = false,
                            viewId = storedView.Id.IntegerValue,
                            elementId = storedView.Id.IntegerValue,
                            uniqueId = storedView.UniqueId ?? string.Empty,
                            name = storedView.Name ?? string.Empty
                        };
                    }
                }

                // Revit制約の事前チェック
                if (baseView is ViewSheet)
                    return new { ok = false, msg = "シート(ViewSheet)は複製できません。" };

                if (baseView.ViewType == ViewType.ProjectBrowser)
                    return new { ok = false, msg = "Project Browser は対象外です。" };

                // 事前: desiredName がある場合の衝突ポリシー
                View? existingByName = null;
                if (!string.IsNullOrWhiteSpace(desiredName))
                {
                    existingByName = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .FirstOrDefault(v => !v.IsTemplate && string.Equals(v.Name, desiredName, StringComparison.OrdinalIgnoreCase));

                    if (existingByName != null && onNameConflict == "returnexisting")
                    {
                        // Return existing without creating a new view
                        var ev = existingByName;
                        if (!string.IsNullOrWhiteSpace(idemKey)) Core.IdempotencyRegistry.Set(idemKey!, ev.UniqueId);
                        return new DuplicateViewResultDto
                        {
                            ok = true,
                            created = false,
                            viewId = ev.Id.IntegerValue,
                            elementId = ev.Id.IntegerValue,
                            uniqueId = ev.UniqueId ?? string.Empty,
                            name = ev.Name ?? string.Empty,
                            conflict = "returnExisting"
                        };
                    }
                }

                // ここから複製。WithDetailing で失敗したら Duplicate に自動フォールバック
                ElementId dupId;

                using (var t = new Transaction(doc, "Duplicate View (Simple)"))
                {
                    t.Start();
                    try
                    {
                        var opt = withDetailing ? ViewDuplicateOption.WithDetailing
                                                : ViewDuplicateOption.Duplicate;
                        dupId = baseView.Duplicate(opt);
                    }
                    catch (Exception)
                    {
                        if (withDetailing)
                        {
                            // 明細付きがNGなビュー種別のため通常複製にフォールバック
                            dupId = baseView.Duplicate(ViewDuplicateOption.Duplicate);
                        }
                        else
                        {
                            throw; // 既に通常複製で失敗しているので上位へ
                        }
                    }

                    var v = doc.GetElement(dupId) as View;
                    if (v == null)
                    {
                        t.RollBack();
                        return new { ok = false, msg = "Duplicated view was not found." };
                    }

                    // 名前付け
                    string finalName;
                    if (!string.IsNullOrWhiteSpace(desiredName))
                    {
                        // desiredName 指定時は onNameConflict に従う
                        finalName = desiredName!;
                        if (existingByName != null && onNameConflict == "fail")
                        {
                            t.RollBack();
                            return new { ok = false, code = "DUPLICATE_NAME", msg = $"A view named '{desiredName}' already exists." };
                        }
                        if (existingByName != null && onNameConflict == "timestamp")
                        {
                            finalName = desiredName + " " + DateTime.Now.ToString("HHmmss");
                        }
                        if (existingByName != null && onNameConflict == "increment")
                        {
                            // Append (n)
                            int n = 2;
                            while (n < 1000)
                            {
                                var candidate = $"{desiredName} ({n})";
                                bool exists = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                                    .Any(x => !x.IsTemplate && string.Equals(x.Name, candidate, StringComparison.OrdinalIgnoreCase));
                                if (!exists) { finalName = candidate; break; }
                                n++;
                            }
                        }
                        // Apply
                        try { v.Name = finalName; } catch { /* if still conflicts, keep Revit-assigned */ }
                    }
                    else
                    {
                        // 互換：namePrefix 優先、無ければ "BaseName Copy HHmmss"
                        string suggested =
                            string.IsNullOrWhiteSpace(namePrefix)
                            ? (baseView.Name + " Copy " + DateTime.Now.ToString("HHmmss"))
                            : (namePrefix + DateTime.Now.ToString("HHmmss"));
                        try { v.Name = suggested; } catch { /* ignore name conflicts */ }
                    }

                    t.Commit();

                    // 成功返却（camelCase固定、elementIdも互換のため同値で返す）
                    var dto = new DuplicateViewResultDto
                    {
                        ok = true,
                        viewId = v.Id.IntegerValue,
                        elementId = v.Id.IntegerValue,
                        uniqueId = v.UniqueId ?? string.Empty,
                        name = v.Name ?? string.Empty,
                        created = true
                    };
                    if (!string.IsNullOrWhiteSpace(idemKey)) Core.IdempotencyRegistry.Set(idemKey!, v.UniqueId ?? string.Empty);
                    return dto;
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException ocex)
            {
                return new { ok = false, msg = "Operation canceled: " + ocex.Message };
            }
            catch (Exception ex)
            {
                // 例外の詳細はログ側に残る前提。ユーザー/エージェントには要点のみ
                return new { ok = false, msg = "Duplicate failed: " + ex.Message };
            }
        }
    }
}
