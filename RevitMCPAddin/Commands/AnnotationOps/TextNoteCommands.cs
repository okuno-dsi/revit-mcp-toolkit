// ================================================================
// File: Commands/AnnotationOps/TextNoteCommands.cs
// Purpose : Create / Edit TextNote and Update Parameters (Spec-aware)
//           - 数値入力は既定で「プロジェクトの表示単位」を使用
//           - "unit" で mm, cm, m, in, ft, deg を上書き可能
//           - Parameter の Spec に応じて外部値→内部値へ変換
// Target  : .NET Framework 4.8 / Revit 2023+
// Depends : Autodesk.Revit.DB, Autodesk.Revit.UI, Newtonsoft.Json.Linq
//           RevitMCPAddin.Core (IRevitCommandHandler, RequestCommand, RevitLogger)
// Notes   : Revit 2023 API では Units/FormatOptions→UnitTypeId を経由して
//           UnitUtils.ConvertToInternalUnits(...) を使用します。
// ================================================================

#nullable enable
using System;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core; // IRevitCommandHandler, RequestCommand, RevitLogger など

namespace RevitMCPAddin.Commands.AnnotationOps
{
    public class TextNoteCommands : IRevitCommandHandler
    {
        // 複数メソッドを '|' で公開（既存のルーター方針に合わせる）
        public string CommandName => "create_text_note|set_text|update_text_note_parameter|move_text_note|delete_text_note";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var method = (cmd.Method ?? "").Trim();
                switch (method)
                {
                    case "create_text_note": return CreateTextNote(uiapp, cmd);
                    case "set_text": return SetText(uiapp, cmd);
                    case "update_text_note_parameter": return UpdateTextNoteParameter(uiapp, cmd);
                    case "move_text_note": return MoveTextNote(uiapp, cmd);
                    case "delete_text_note": return DeleteTextNote(uiapp, cmd);
                    default:
                        return new { ok = false, code = -32601, msg = $"Unknown command: {method}" };
                }
            }
            catch (Exception ex)
            {
                RevitLogger.Error($"TextNoteCommands exception: {ex}");
                return new { ok = false, msg = "Exception", detail = ex.Message };
            }
        }

        // ------------------------------------------------------------
        // create_text_note
        // params: { viewId:int?, text:string, x:number, y:number, z?:number, typeName?:string, unit?:"mm|cm|m|in|ft" }
        // 座標は既定で「プロジェクトの長さ表示単位」を使用。unit指定で上書き可。
        // ------------------------------------------------------------
        private object CreateTextNote(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = (JObject?)cmd.Params ?? new JObject();
            // Optional refresh after operation
            bool refreshView = p.Value<bool?>("refreshView") ?? false;

            // Batch mode support: items[] with time-slice controls
            var itemsArr = p["items"] as JArray;
            if (itemsArr != null && itemsArr.Count > 0)
            {
                int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
                int batchSize = Math.Max(1, p.Value<int?>("batchSize") ?? 50);
                int maxMillis = Math.Max(0, p.Value<int?>("maxMillisPerTx") ?? 100);

                var created = new System.Collections.Generic.List<object>();
                int next = startIndex;
                var sw = System.Diagnostics.Stopwatch.StartNew();

                using (var t = new Transaction(doc, "create_text_note(batch)"))
                {
                    t.Start();
                    int processed = 0;
                    for (int i = startIndex; i < itemsArr.Count; i++)
                    {
                        var it = itemsArr[i] as JObject ?? new JObject();
                        var res = CreateOneTextNote(doc, it);
                        if (res.ok)
                        {
                            created.Add(new { elementId = res.elementId, viewId = res.viewId, typeId = res.typeId });
                        }
                        processed++;
                        next = i + 1;
                        if (processed >= batchSize) break;
                        if (maxMillis > 0 && sw.ElapsedMilliseconds >= maxMillis) break;
                    }
                    t.Commit();
                }

                if (refreshView)
                {
                    try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { }
                }

                bool completed = (next >= itemsArr.Count);
                return new { ok = true, countCreated = created.Count, created, completed, nextIndex = completed ? (int?)null : next };
            }
            int? viewIdOpt = p.Value<int?>("viewId");
            string text = (p.Value<string>("text") ?? "").Trim();
            if (string.IsNullOrEmpty(text)) return new { ok = false, msg = "text required." };

            string? unitOpt = p.Value<string>("unit"); // mm, cm, m, in, ft
            double xExt = p.Value<double?>("x") ?? 0.0;
            double yExt = p.Value<double?>("y") ?? 0.0;
            double zExt = p.Value<double?>("z") ?? 0.0;

            View view = null;
            if (viewIdOpt.HasValue)
            {
                view = doc.GetElement(new ElementId(viewIdOpt.Value)) as View;
            }
            else
            {
                view = doc.ActiveView;
            }
            if (view == null) return new { ok = false, msg = "View not found." };
            if (!(view is ViewPlan) && !(view is View3D) && !(view is ViewSection) && !(view is ViewDrafting))
                return new { ok = false, msg = $"Unsupported view type for TextNote: {view.GetType().Name}" };

            // 位置は Length の表示単位で入力されるものとして内部値へ
            var pos = new XYZ(
                UnitsHelper.ToInternalByProjectUnits(doc, SpecTypeId.Length, xExt, unitOpt),
                UnitsHelper.ToInternalByProjectUnits(doc, SpecTypeId.Length, yExt, unitOpt),
                UnitsHelper.ToInternalByProjectUnits(doc, SpecTypeId.Length, zExt, unitOpt)
            );

            // タイプ解決（任意）
            TextNoteType? tnt = null;
            string? typeName = p.Value<string>("typeName");
            if (!string.IsNullOrWhiteSpace(typeName))
            {
                tnt = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
                      .FirstOrDefault(x => x.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
                if (tnt == null) return new { ok = false, msg = $"TextNoteType not found: '{typeName}'" };
            }
            else
            {
                tnt = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().FirstOrDefault();
                if (tnt == null) return new { ok = false, msg = "No TextNoteType in document." };
            }

            using (var t = new Transaction(doc, "create_text_note"))
            {
                t.Start();
                var opts = new TextNoteOptions(tnt.Id) { HorizontalAlignment = HorizontalTextAlignment.Left };
                var tn = TextNote.Create(doc, view.Id, pos, text, opts);
                t.Commit();
                if (refreshView)
                {
                    try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { }
                }
                return new { ok = true, elementId = tn.Id.IntegerValue, viewId = view.Id.IntegerValue, typeId = tnt.Id.IntegerValue };
            }
        }

        private static (bool ok, string msg, int elementId, int viewId, int typeId) CreateOneTextNote(Document doc, JObject p)
        {
            int? viewIdOpt = p.Value<int?>("viewId");
            string text = (p.Value<string>("text") ?? "").Trim();
            if (string.IsNullOrEmpty(text)) return (false, "text required.", 0, 0, 0);

            string? unitOpt = p.Value<string>("unit");
            bool refreshView = p.Value<bool?>("refreshView") ?? false;
            double xExt = p.Value<double?>("x") ?? 0.0;
            double yExt = p.Value<double?>("y") ?? 0.0;
            double zExt = p.Value<double?>("z") ?? 0.0;

            View view = viewIdOpt.HasValue
                ? doc.GetElement(new ElementId(viewIdOpt.Value)) as View
                : doc.ActiveView;
            if (view == null) return (false, "View not found.", 0, 0, 0);
            if (!(view is ViewPlan) && !(view is View3D) && !(view is ViewSection) && !(view is ViewDrafting))
                return (false, $"Unsupported view type for TextNote: {view.GetType().Name}", 0, 0, 0);

            var pos = new XYZ(
                UnitsHelper.ToInternalByProjectUnits(doc, SpecTypeId.Length, xExt, unitOpt),
                UnitsHelper.ToInternalByProjectUnits(doc, SpecTypeId.Length, yExt, unitOpt),
                UnitsHelper.ToInternalByProjectUnits(doc, SpecTypeId.Length, zExt, unitOpt)
            );

            TextNoteType? tnt = null;
            string? typeName = p.Value<string>("typeName");
            if (!string.IsNullOrWhiteSpace(typeName))
            {
                tnt = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
                      .FirstOrDefault(x => x.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
                if (tnt == null) return (false, $"TextNoteType not found: '{typeName}'", 0, 0, 0);
            }
            else
            {
                tnt = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().FirstOrDefault();
                if (tnt == null) return (false, "No TextNoteType in document.", 0, 0, 0);
            }

            var opts = new TextNoteOptions(tnt.Id) { HorizontalAlignment = HorizontalTextAlignment.Left };
            var tn = TextNote.Create(doc, view.Id, pos, text, opts);
            return (true, string.Empty, tn.Id.IntegerValue, view.Id.IntegerValue, tnt.Id.IntegerValue);
        }

        // ------------------------------------------------------------
        // set_text
        // params: { elementId:int, text:string }
        // ------------------------------------------------------------
        private object SetText(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = (JObject?)cmd.Params ?? new JObject();
            int elementId = p.Value<int?>("elementId") ?? 0;
            if (elementId <= 0) return new { ok = false, msg = "elementId required." };
            bool refreshView = p.Value<bool?>("refreshView") ?? false;
            string text = (p.Value<string>("text") ?? "").Trim();

            var tn = doc.GetElement(new ElementId(elementId)) as TextNote;
            if (tn == null) return new { ok = false, msg = $"TextNote not found: {elementId}" };

            using (var t = new Transaction(doc, "set_text"))
            {
                t.Start();
                tn.Text = text;
                t.Commit();
                if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
                return new { ok = true, elementId = tn.Id.IntegerValue, len = text.Length };
            }
        }

        // ------------------------------------------------------------
        // update_text_note_parameter
        // params: {
        //   elementId:int, paramName:string, value:any, applyToType?:bool,
        //   unit?: "mm|cm|m|in|ft|deg" (数値のときのみ有効。未指定はプロジェクト表示単位)
        // }
        // 備考: paramName はローカライズ名でOK（英語/日本語環境どちらでも可）
        // ------------------------------------------------------------
        private object UpdateTextNoteParameter(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = (JObject?)cmd.Params ?? new JObject();
            int elementId = p.Value<int?>("elementId") ?? 0;
            if (elementId <= 0) return new { ok = false, msg = "elementId required." };

            string paramName = (p.Value<string>("paramName") ?? "").Trim();
            if (string.IsNullOrEmpty(paramName)) return new { ok = false, msg = "paramName required." };

            JToken? valueTok = p["value"];
            string? unitOpt = p.Value<string>("unit"); // 数値時の明示単位
            bool applyToType = p.Value<bool?>("applyToType") ?? false;
            bool refreshView = p.Value<bool?>("refreshView") ?? false;

            var elem = doc.GetElement(new ElementId(elementId)) as TextNote;
            if (elem == null) return new { ok = false, msg = $"TextNote not found: {elementId}" };

            Element target = elem;
            if (applyToType)
            {
                var type = doc.GetElement(elem.GetTypeId());
                if (type == null) return new { ok = false, msg = "Type not found for TextNote." };
                target = type;
            }

            var param = FindParameterByName(target, paramName);
            if (param == null) return new { ok = false, msg = $"Parameter not found: '{paramName}' (target={(applyToType ? "type" : "instance")})" };
            if (param.IsReadOnly) return new { ok = false, msg = $"Parameter '{paramName}' is read-only." };

            using (var t = new Transaction(doc, "update_text_note_parameter"))
            {
                t.Start();

                bool ok;
                string? reason = null;

                try
                {
                    ok = SetParameterWithProjectUnits(doc, param, valueTok, unitOpt, out reason);
                }
                catch (Exception ex)
                {
                    ok = false;
                    reason = ex.Message;
                }

                if (!ok)
                {
                    t.RollBack();
                    return new { ok = false, msg = $"Failed to set '{paramName}'", reason };
                }

                t.Commit();
                if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
                return new
                {
                    ok = true,
                    elementId = elem.Id.IntegerValue,
                    target = applyToType ? "type" : "instance",
                    param = param.Definition?.Name
                };
            }
        }

        // ------------------------------------------------------------
        // move_text_note
        // params: { elementId:int, dx:number, dy:number, dz?:number, unit?:"mm|cm|m|in|ft" }
        // ------------------------------------------------------------
        private object MoveTextNote(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = (JObject?)cmd.Params ?? new JObject();
            int elementId = p.Value<int?>("elementId") ?? 0;
            if (elementId <= 0) return new { ok = false, msg = "elementId required." };

            double dxExt = p.Value<double?>("dx") ?? 0.0;
            double dyExt = p.Value<double?>("dy") ?? 0.0;
            double dzExt = p.Value<double?>("dz") ?? 0.0;
            string? unitOpt = p.Value<string>("unit");
            bool refreshView = p.Value<bool?>("refreshView") ?? false;

            var tn = doc.GetElement(new ElementId(elementId)) as TextNote;
            if (tn == null) return new { ok = false, msg = $"TextNote not found: {elementId}" };

            var v = new XYZ(
                UnitsHelper.ToInternalByProjectUnits(doc, SpecTypeId.Length, dxExt, unitOpt),
                UnitsHelper.ToInternalByProjectUnits(doc, SpecTypeId.Length, dyExt, unitOpt),
                UnitsHelper.ToInternalByProjectUnits(doc, SpecTypeId.Length, dzExt, unitOpt)
            );

            using (var t = new Transaction(doc, "move_text_note"))
            {
                t.Start();
                ElementTransformUtils.MoveElement(doc, tn.Id, v);
                t.Commit();
                if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
                return new { ok = true, elementId = tn.Id.IntegerValue };
            }
        }

        // ------------------------------------------------------------
        // delete_text_note
        // params: { elementId:int }
        // ------------------------------------------------------------
        private object DeleteTextNote(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = (JObject?)cmd.Params ?? new JObject();
            int elementId = p.Value<int?>("elementId") ?? 0;
            if (elementId <= 0) return new { ok = false, msg = "elementId required." };
            bool refreshView = p.Value<bool?>("refreshView") ?? false;

            var tn = doc.GetElement(new ElementId(elementId)) as TextNote;
            if (tn == null) return new { ok = false, msg = $"TextNote not found: {elementId}" };

            using (var t = new Transaction(doc, "delete_text_note"))
            {
                t.Start();
                doc.Delete(tn.Id);
                t.Commit();
                if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
                return new { ok = true, deleted = elementId };
            }
        }

        // ========= Helpers =========

        /// <summary>パラメータ名（英日両対応想定）で Parameter を取得</summary>
        private static Parameter? FindParameterByName(Element e, string paramName)
        {
            // 内蔵・共有ともに名称照合（大文字小文字無視）
            foreach (Parameter p in e.Parameters)
            {
                string n = p.Definition?.Name ?? "";
                if (string.Equals(n, paramName, StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            return null;
        }

        /// <summary>
        /// valueTok を Parameter.StorageType と Spec に応じて内部値で Set する。
        /// unitOpt 指定があればそれを優先。未指定はプロジェクトの表示単位を使用。
        /// </summary>
        private static bool SetParameterWithProjectUnits(Document doc, Parameter param, JToken? valueTok, string? unitOpt, out string? reason)
        {
            reason = null;
            if (valueTok == null || valueTok.Type == JTokenType.Null)
            {
                reason = "value is null";
                return false;
            }

            switch (param.StorageType)
            {
                case StorageType.Double:
                    {
                        // Spec（ForgeTypeId）を可能なら取得
                        ForgeTypeId? spec = TryGetSpecTypeId(param.Definition);

                        // 数値のみ受け付ける（文字列は将来拡張：数式/単位付きテキスト等）
                        if (valueTok.Type != JTokenType.Float && valueTok.Type != JTokenType.Integer)
                        {
                            reason = "numeric value required for double parameter";
                            return false;
                        }
                        double ext = valueTok.Value<double>();

                        // データ型が分かれば Spec に従って変換（Length/Area/Angle…）
                        double internalVal;
                        if (spec != null)
                        {
                            internalVal = UnitsHelper.ToInternalByProjectUnits(doc, spec, ext, unitOpt);
                        }
                        else
                        {
                            // 取得失敗時は安全側フォールバック：Length とみなす
                            internalVal = UnitsHelper.ToInternalByProjectUnits(doc, SpecTypeId.Length, ext, unitOpt);
                        }

                        return param.Set(internalVal);
                    }
                case StorageType.Integer:
                    {
                        int iv;
                        if (valueTok.Type == JTokenType.Boolean)
                        {
                            iv = valueTok.Value<bool>() ? 1 : 0;
                        }
                        else if (valueTok.Type == JTokenType.Integer)
                        {
                            iv = valueTok.Value<int>();
                        }
                        else
                        {
                            reason = "integer or boolean value required for integer parameter";
                            return false;
                        }
                        return param.Set(iv);
                    }
                case StorageType.String:
                    {
                        string sv = valueTok.Type == JTokenType.String ? valueTok.Value<string>() ?? "" : valueTok.ToString();
                        return param.Set(sv);
                    }
                case StorageType.ElementId:
                    {
                        // 直接 ElementId を渡す or null/-1 で解除
                        if (valueTok.Type == JTokenType.Integer)
                        {
                            int id = valueTok.Value<int>();
                            return param.Set(new ElementId(id));
                        }
                        if (valueTok.Type == JTokenType.Null || (valueTok.Type == JTokenType.String && string.IsNullOrWhiteSpace(valueTok.Value<string>())))
                        {
                            return param.Set(ElementId.InvalidElementId);
                        }
                        reason = "elementId (int) required for elementId parameter";
                        return false;
                    }
                default:
                    reason = "unsupported storage type";
                    return false;
            }
        }

        /// <summary>Definition から ForgeTypeId(Spec) をなるべく取得（反射フォールバック含む）</summary>
        private static ForgeTypeId? TryGetSpecTypeId(Definition? def)
        {
            if (def == null) return null;
            try
            {
                // Revit 2023+: Definition.GetDataType()
                var mi = def.GetType().GetMethod("GetDataType")
                      ?? def.GetType().GetMethod("GetDataTypeId"); // 環境差吸収
                var obj = mi?.Invoke(def, null);
                return obj as ForgeTypeId;
            }
            catch { return null; }
        }

        // -------------------- Units Helper --------------------
        private static class UnitsHelper
        {
            /// <summary>
            /// 数値 external を、unitOpt 指定があればその単位、なければ
            /// プロジェクトの「表示単位」で読み替え、内部値へ変換します。
            /// </summary>
            public static double ToInternalByProjectUnits(Document doc, ForgeTypeId spec, double external, string? unitOpt)
            {
                if (!string.IsNullOrWhiteSpace(unitOpt))
                {
                    // 明示単位 優先
                    var unitId = MapUnitOpt(spec, unitOpt!);
                    return UnitUtils.ConvertToInternalUnits(external, unitId);
                }

                // プロジェクトの表示単位（FormatOptions）を取得
                Units u = doc.GetUnits();
                var fo = u.GetFormatOptions(spec);
                var disp = fo.GetUnitTypeId(); // UnitTypeId
                return UnitUtils.ConvertToInternalUnits(external, disp);
            }

            /// <summary>unitOpt 文字列を UnitTypeId にマップ（Specを考慮）</summary>
            private static ForgeTypeId MapUnitOpt(ForgeTypeId spec, string unitOpt)
            {
                if (unitOpt == null) unitOpt = string.Empty;
                unitOpt = unitOpt.Trim().ToLowerInvariant();

                // 角度（Angle）のみ
                if (spec == SpecTypeId.Angle)
                {
                    if (unitOpt == "deg" || unitOpt == "degree" || unitOpt == "degrees")
                        return UnitTypeId.Degrees;   // ← これは ForgeTypeId 値
                    if (unitOpt == "rad" || unitOpt == "radian" || unitOpt == "radians")
                        return UnitTypeId.Radians;
                    return UnitTypeId.Degrees; // 既定
                }

                // 長さほか
                switch (unitOpt)
                {
                    case "mm": return UnitTypeId.Millimeters;
                    case "cm": return UnitTypeId.Centimeters;
                    case "m": return UnitTypeId.Meters;
                    case "in": return UnitTypeId.Inches;
                    case "ft": return UnitTypeId.Feet;
                    default: return UnitTypeId.Millimeters; // セーフデフォルト
                }
            }
        }
    }
}
