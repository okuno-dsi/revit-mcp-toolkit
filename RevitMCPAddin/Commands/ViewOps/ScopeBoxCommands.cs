// ============================================================================
// File   : Commands/ViewOps/ScopeBoxCommands.cs
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Purpose: Scope Box（スコープボックス）関連コマンド群を「1ファイル」に集約
// Notes  : Revit API（～2025時点）では ScopeBox の新規作成や寸法編集APIは未公開。
//           - できる：一覧取得、ビューへの割当/解除、割当確認
//           - できない：新規作成、位置・サイズ・回転の直接編集、名称変更（原則）
//           できない操作は ok:false と明確な理由を返します。
// Depends: IRevitCommandHandler / RequestCommand
//          ※ RevitLogger / UnitHelper は存在すれば反射で利用、無ければ安全フォールバック。
// ============================================================================

#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ViewOps
{
    // ================================
    // 共通ユーティリティ（同一ファイル内）
    // ================================
    internal static class SafeLog
    {
        // 代表的な候補名（プロジェクト差吸収）
        private static readonly string[] InfoNames = { "LogInfo", "Info", "Log", "AppendLog", "WriteLine" };
        private static readonly string[] ErrorNames = { "LogError", "Error", "Log", "AppendLog", "WriteLine" };

        private static Type _loggerType;
        private static MethodInfo _infoMethod;
        private static MethodInfo _errorMethod;

        private static void EnsureLogger()
        {
            if (_loggerType != null) return;

            // 代表的な型名を順に探す
            string[] typeCandidates =
            {
                "RevitMCPAddin.Core.RevitLogger",
                "RevitMCPAddin.RevitLogger",
                "RevitMCPAddin.Core.Logger",
                "RevitMCPAddin.Logger"
            };

            foreach (var tn in typeCandidates)
            {
                _loggerType = Type.GetType(tn);
                if (_loggerType != null) break;
            }

            if (_loggerType == null) return;

            // info/error 系を探す（string 1引数の static メソッド）
            foreach (var name in InfoNames)
            {
                _infoMethod = _loggerType.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string) }, null);
                if (_infoMethod != null) break;
            }
            foreach (var name in ErrorNames)
            {
                _errorMethod = _loggerType.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string) }, null);
                if (_errorMethod != null) break;
            }
        }

        public static void Info(string message)
        {
            try
            {
                EnsureLogger();
                if (_infoMethod != null)
                {
                    _infoMethod.Invoke(null, new object[] { message });
                    return;
                }
            }
            catch { /* ignore and fallback */ }

            System.Diagnostics.Debug.WriteLine("[INFO] " + message);
            Trace.WriteLine("[INFO] " + message);
        }

        public static void Error(string message)
        {
            try
            {
                EnsureLogger();
                if (_errorMethod != null)
                {
                    _errorMethod.Invoke(null, new object[] { message });
                    return;
                }
            }
            catch { /* ignore and fallback */ }

            System.Diagnostics.Debug.WriteLine("[ERROR] " + message);
            Trace.WriteLine("[ERROR] " + message);
        }
    }

    internal static class ScopeBoxUtil
    {
        public static object Fail(string msg, string notes = null)
            => new { ok = false, msg, notes };

        public static string SafeName(Element e)
        {
            try { return e.Name ?? "(no name)"; } catch { return "(no name)"; }
        }

        public static BoundingBoxXYZ SafeGetBBox(Element e)
        {
            try { return e.get_BoundingBox(null); } catch { return null; }
        }

        // UnitHelper.FtToMm(double) があれば反射で利用。無ければ 304.8 倍。
        public static double FtToMm(double ft)
        {
            try
            {
                var t = Type.GetType("RevitMCPAddin.Core.UnitHelper") ?? Type.GetType("RevitMCPAddin.UnitHelper");
                var m = t?.GetMethod("FtToMm", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(double) }, null);
                if (m != null)
                {
                    var r = m.Invoke(null, new object[] { ft });
                    if (r is double d) return d;
                }
            }
            catch { /* ignore */ }
            return ft * 304.8;
        }

        public static object BBoxPayload(BoundingBoxXYZ bbox)
        {
            if (bbox == null) return null;
            return new
            {
                bboxFt = new
                {
                    min = new { x = bbox.Min.X, y = bbox.Min.Y, z = bbox.Min.Z },
                    max = new { x = bbox.Max.X, y = bbox.Max.Y, z = bbox.Max.Z }
                },
                bboxMm = new
                {
                    min = new { x = FtToMm(bbox.Min.X), y = FtToMm(bbox.Min.Y), z = FtToMm(bbox.Min.Z) },
                    max = new { x = FtToMm(bbox.Max.X), y = FtToMm(bbox.Max.Y), z = FtToMm(bbox.Max.Z) }
                }
            };
        }

        public static ElementId? GetAssignedScopeBoxId(View v)
        {
            var p = v.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
            if (p == null) return null;
            var id = p.AsElementId();
            if (id == null || id == ElementId.InvalidElementId) return null;
            return id;
        }
    }

    // ================================
    // 1) list_scope_boxes
    // ================================
    public sealed class ListScopeBoxesHandler : IRevitCommandHandler
    {
        public string CommandName => "list_scope_boxes";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return ScopeBoxUtil.Fail("No active document.");

            var items = new List<object>();
            var col = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                .WhereElementIsNotElementType()
                .ToElements();

            foreach (var e in col)
            {
                var bbox = ScopeBoxUtil.SafeGetBBox(e);
                items.Add(new
                {
                    id = e.Id.IntValue(),
                    uniqueId = e.UniqueId,
                    name = ScopeBoxUtil.SafeName(e),
                    bbox = ScopeBoxUtil.BBoxPayload(bbox)
                });
            }

            return new
            {
                ok = true,
                count = items.Count,
                items,
                units = new { Length = "mm (ft raw included)" }
            };
        }
    }

    // ================================
    // 2) get_view_scope_box
    // ================================
    public sealed class GetViewScopeBoxHandler : IRevitCommandHandler
    {
        public string CommandName => "get_view_scope_box";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = cmd.Params as JObject ?? new JObject();
            var viewId = p.Value<int?>("viewId");
            if (viewId == null) return ScopeBoxUtil.Fail("viewId is required.");

            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return ScopeBoxUtil.Fail("No active document.");

            var v = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId.Value)) as View;
            if (v == null) return ScopeBoxUtil.Fail($"View not found: {viewId.Value}");

            var sbId = ScopeBoxUtil.GetAssignedScopeBoxId(v);
            if (sbId == null) return new { ok = true, hasScopeBox = false, scopeBox = (object)null };

            var e = doc.GetElement(sbId);
            if (e == null) return new { ok = true, hasScopeBox = false, scopeBox = (object)null };

            var bbox = ScopeBoxUtil.SafeGetBBox(e);
            return new
            {
                ok = true,
                hasScopeBox = true,
                scopeBox = new
                {
                    id = e.Id.IntValue(),
                    uniqueId = e.UniqueId,
                    name = ScopeBoxUtil.SafeName(e),
                    bbox = ScopeBoxUtil.BBoxPayload(bbox)
                }
            };
        }
    }

    // ================================
    // 3) assign_scope_box
    // ================================
    public sealed class AssignScopeBoxHandler : IRevitCommandHandler
    {
        public string CommandName => "assign_scope_box";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = cmd.Params as JObject ?? new JObject();
            var viewId = p.Value<int?>("viewId");
            var scopeBoxId = p.Value<int?>("scopeBoxId");
            if (viewId == null || scopeBoxId == null)
                return ScopeBoxUtil.Fail("viewId and scopeBoxId are required.");

            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return ScopeBoxUtil.Fail("No active document.");

            var v = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId.Value)) as View;
            if (v == null) return ScopeBoxUtil.Fail($"View not found: {viewId.Value}");

            var sb = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(scopeBoxId.Value));
            if (sb == null) return ScopeBoxUtil.Fail($"ScopeBox not found: {scopeBoxId.Value}");

            var param = v.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
            if (param == null || param.IsReadOnly)
                return ScopeBoxUtil.Fail("This view does not support scope box assignment or the parameter is read-only.");

            using (var t = new Transaction(doc, "Assign Scope Box"))
            {
                t.Start();
                var ok = param.Set(sb.Id);
                t.Commit();
                if (!ok) return ScopeBoxUtil.Fail("Failed to assign scope box (Set returned false).");
            }

            SafeLog.Info($"Assigned ScopeBox {scopeBoxId.Value} to View {viewId.Value}");
            return new { ok = true, viewId = viewId.Value, scopeBoxId = scopeBoxId.Value };
        }
    }

    // ================================
    // 4) clear_scope_box
    // ================================
    public sealed class ClearScopeBoxHandler : IRevitCommandHandler
    {
        public string CommandName => "clear_scope_box";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = cmd.Params as JObject ?? new JObject();
            var viewId = p.Value<int?>("viewId");
            if (viewId == null) return ScopeBoxUtil.Fail("viewId is required.");

            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return ScopeBoxUtil.Fail("No active document.");

            var v = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId.Value)) as View;
            if (v == null) return ScopeBoxUtil.Fail($"View not found: {viewId.Value}");

            var param = v.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
            if (param == null || param.IsReadOnly)
                return ScopeBoxUtil.Fail("This view does not support scope box assignment or the parameter is read-only.");

            using (var t = new Transaction(doc, "Clear Scope Box"))
            {
                t.Start();
                var ok = param.Set(ElementId.InvalidElementId);
                t.Commit();
                if (!ok) return ScopeBoxUtil.Fail("Failed to clear scope box (Set returned false).");
            }

            SafeLog.Info($"Cleared ScopeBox from View {viewId.Value}");
            return new { ok = true, viewId = viewId.Value, cleared = true };
        }
    }

    // ================================
    // 5) create_scope_box（API非対応スタブ）
    // ================================
    public sealed class CreateScopeBoxHandler : IRevitCommandHandler
    {
        public string CommandName => "create_scope_box";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            return ScopeBoxUtil.Fail(
                "Scope Box creation is not supported by Revit API (as of 2025).",
                notes: "Workarounds: (1) Prepare templates manually and use assign/clear, (2) SectionBox is a different feature and not included here, (3) Duplicating/transforming existing Scope Boxes is unreliable."
            );
        }
    }

    // ================================
    // 6) update_scope_box（API非対応スタブ）
    // ================================
    public sealed class UpdateScopeBoxHandler : IRevitCommandHandler
    {
        public string CommandName => "update_scope_box";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            return ScopeBoxUtil.Fail(
                "Editing Scope Box extents/transform is not exposed by Revit API.",
                notes: "No public API for handle-like edits. Consider using pre-made Scope Boxes and assign per view."
            );
        }
    }

    // ================================
    // 7) rename_scope_box（原則不可スタブ）
    // ================================
    public sealed class RenameScopeBoxHandler : IRevitCommandHandler
    {
        public string CommandName => "rename_scope_box";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = cmd.Params as JObject ?? new JObject();
            var scopeBoxId = p.Value<int?>("scopeBoxId");
            var newName = p.Value<string>("newName");
            if (scopeBoxId == null || string.IsNullOrWhiteSpace(newName))
                return ScopeBoxUtil.Fail("scopeBoxId and newName are required.");

            return ScopeBoxUtil.Fail(
                "Renaming Scope Box via API is not reliably supported (often read-only).",
                notes: "If naming is required, prepare named templates manually and assign per view."
            );
        }
    }
}


