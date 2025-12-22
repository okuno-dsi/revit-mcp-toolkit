// ================================================================
// 3DViewCommands.cs
// Revit 2023 / .NET Framework 4.8 / C# 8
// 概要:
//   - create_3d_view            : 正投影(Isometric) 3Dビューの作成
//   - create_perspective_view   : カメラ(パース) 3Dビューの作成（eye/targetはmm）
//   - create_walkthrough        : ウォークスルー作成（安全ガード: 未サポート時は丁寧にNGを返す）
// 依存:
//   - Autodesk.Revit.DB, Autodesk.Revit.UI
//   - Newtonsoft.Json.Linq
//   - RevitMCPAddin.Core (IRevitCommandHandler, RequestCommand)
// 仕様:
//   - すべての座標入力は mm、内部で ft に変換してから処理
//   - name 重複は自動的に "(2)", "(3)" を付与して回避
//   - templateViewId 指定時は ViewTemplate を適用（失敗時は msg に説明）
//   - 例外時は { ok:false, msg:"..." } を返却
// ================================================================
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using static Autodesk.Revit.DB.UnitUtils;

namespace RevitMCPAddin.Commands.ViewOps
{
    internal static class View3DUtil
    {
        public static double MmToFt(double mm) => ConvertToInternalUnits(mm, UnitTypeId.Millimeters);

        public static XYZ Mm(XYZ mm) => new XYZ(MmToFt(mm.X), MmToFt(mm.Y), MmToFt(mm.Z));

        public static ViewFamilyType Find3DViewFamilyType(Document doc)
        {
            // View3D.CreateIsometric / CreatePerspective の Type は ViewFamily=ThreeDimensional が必要
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);
        }

        public static string MakeUniqueViewName(Document doc, string desired)
        {
            if (string.IsNullOrWhiteSpace(desired)) return desired;
            string name = desired;
            int i = 2;
            while (IsViewNameExists(doc, name))
            {
                name = $"{desired} ({i++})";
            }
            return name;
        }

        private static bool IsViewNameExists(Document doc, string name)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Any(v => !v.IsTemplate && string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        public static void TryApplyViewTemplate(View view, int templateViewId)
        {
            if (templateViewId <= 0) return;
            var doc = view.Document;
            var tpl = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(templateViewId)) as View;
            if (tpl == null || !tpl.IsTemplate) throw new InvalidOperationException("templateViewId はテンプレートビューを指している必要があります。");
            view.ViewTemplateId = tpl.Id;
        }
    }

    // ------------------------------------------------------------
    // 1) 正投影 3D ビュー作成
    // ------------------------------------------------------------
    public class Create3DViewCommand : IRevitCommandHandler
    {
        public string CommandName => "create_3d_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

                var p = (JObject)(cmd.Params ?? new JObject());
                string desiredName = (p.Value<string>("name") ?? "").Trim();
                int templateViewId = p.Value<int?>("templateViewId") ?? 0;

                var vft = View3DUtil.Find3DViewFamilyType(doc);
                if (vft == null)
                    return new { ok = false, msg = "3D ビュー用の ViewFamilyType(ThreeDimensional) が見つかりません。" };

                View3D view = null;

                using (var tx = new Transaction(doc, "Create 3D (Isometric)"))
                {
                    tx.Start();
                    view = View3D.CreateIsometric(doc, vft.Id); // Isometric 3D の生成（Revit API 標準） 
                    // 既定名に対して希望名があれば差し替え（重複は自動付番）
                    if (!string.IsNullOrEmpty(desiredName))
                        view.Name = View3DUtil.MakeUniqueViewName(doc, desiredName);

                    // 任意: テンプレート適用
                    if (templateViewId > 0)
                        View3DUtil.TryApplyViewTemplate(view, templateViewId);

                    tx.Commit();
                }

                return new
                {
                    ok = true,
                    viewId = view.Id.IntValue(),
                    name = view.Name,
                    isPerspective = false
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "3Dビューの作成に失敗: " + ex.Message };
            }
        }
    }

    // ------------------------------------------------------------
    // 2) パース（カメラ） 3D ビュー作成
    //    入力: eye(mm), target(mm), up(optional, 省略時は +Z)
    // ------------------------------------------------------------
    public class CreatePerspectiveViewCommand : IRevitCommandHandler
    {
        public string CommandName => "create_perspective_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

                var p = (JObject)(cmd.Params ?? new JObject());
                string desiredName = (p.Value<string>("name") ?? "").Trim();
                int templateViewId = p.Value<int?>("templateViewId") ?? 0;

                var eyeTok = p["eye"] as JObject;
                var tgtTok = p["target"] as JObject;
                if (eyeTok == null || tgtTok == null)
                    return new { ok = false, msg = "eye と target（mm）が必要です。" };

                XYZ eyeMm = new XYZ(eyeTok.Value<double>("x"), eyeTok.Value<double>("y"), eyeTok.Value<double>("z"));
                XYZ tgtMm = new XYZ(tgtTok.Value<double>("x"), tgtTok.Value<double>("y"), tgtTok.Value<double>("z"));

                // up は任意（既定: +Z）
                XYZ upDir = XYZ.BasisZ;
                var upTok = p["up"] as JObject;
                if (upTok != null)
                {
                    var upMm = new XYZ(upTok.Value<double>("x"), upTok.Value<double>("y"), upTok.Value<double>("z"));
                    // 方向ベクトルは単位不要（正規化）
                    upDir = new XYZ(upMm.X, upMm.Y, upMm.Z);
                    if (upDir.IsZeroLength()) upDir = XYZ.BasisZ;
                }

                var vft = View3DUtil.Find3DViewFamilyType(doc);
                if (vft == null)
                    return new { ok = false, msg = "3D ビュー用の ViewFamilyType(ThreeDimensional) が見つかりません。" };

                View3D view = null;

                using (var tx = new Transaction(doc, "Create 3D (Perspective)"))
                {
                    tx.Start();

                    view = View3D.CreatePerspective(doc, vft.Id); // パースビュー生成（API公式）
                    // 名前（重複は自動付番）
                    if (!string.IsNullOrEmpty(desiredName))
                        view.Name = View3DUtil.MakeUniqueViewName(doc, desiredName);

                    // 目/注視/上を設定（mm→ft）
                    var eyeFt = View3DUtil.Mm(eyeMm);
                    var tgtFt = View3DUtil.Mm(tgtMm);
                    var forward = (tgtFt - eyeFt);
                    if (forward.IsZeroLength())
                        throw new InvalidOperationException("eye と target が同一点です。");

                    var orientation = new ViewOrientation3D(
                        eyeFt,
                        upDir.Normalize(),           // 上方向（単位なし）
                        forward.Normalize()          // 前方向（単位なし）
                    );
                    view.SetOrientation(orientation);

                    // 任意: テンプレート適用
                    if (templateViewId > 0)
                        View3DUtil.TryApplyViewTemplate(view, templateViewId);

                    tx.Commit();
                }

                // 応答（eye/target は入力そのまま(mm)で返す）
                return new
                {
                    ok = true,
                    viewId = view.Id.IntValue(),
                    name = view.Name,
                    isPerspective = true,
                    eye = new { x = eyeMm.X, y = eyeMm.Y, z = eyeMm.Z },
                    target = new { x = tgtMm.X, y = tgtMm.Y, z = tgtMm.Z }
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "パースビューの作成に失敗: " + ex.Message };
            }
        }
    }

    // ------------------------------------------------------------
    // 3) ウォークスルー作成（安全ガード版）
    //    備考:
    //      Revit API の Walkthrough はバージョン依存が強く、環境により
    //      型の有無やメソッド差があるため、未対応環境では明示的にNGを返す。
    //      将来的に対応する場合はここを差し替え。
    // ------------------------------------------------------------
    public class CreateWalkthroughCommand : IRevitCommandHandler
    {
        public string CommandName => "create_walkthrough";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

                var p = (JObject)(cmd.Params ?? new JObject());
                string desiredName = (p.Value<string>("name") ?? "").Trim();
                int frameCount = p.Value<int?>("frameCount") ?? 300;

                // 経路点（mm）
                var pathArr = p["pathPoints"] as JArray;
                if (pathArr == null || pathArr.Count < 2)
                    return new { ok = false, msg = "pathPoints（2点以上の配列, mm）が必要です。" };

                // Walkthrough API は Revit バージョン差が大きいため、保守性を優先して
                // 現段階では未サポート扱い（テンプレートは CreatePerspective + ガイド線 等で代替してください）
                return new
                {
                    ok = false,
                    msg = "現在の実装ではウォークスルー(ViewWalkthrough)の自動作成をサポートしていません。代替として create_perspective_view を複数作成し、AI/外部側でシーケンス化してください。",
                    hint = "将来的に対応予定。必要であれば優先度を上げます。"
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "ウォークスルーの作成に失敗: " + ex.Message };
            }
        }
    }
}


