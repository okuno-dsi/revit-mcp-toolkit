// ====================================================================
// File : Commands/CurtainOps/FlipCurtainPanelOrientationCommand.cs
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Purpose: カーテンウォールパネル (OST_CurtainWallPanels) の反転・ミラー・回転
// Notes  : Loadable Panel(FamilyInstance) は Hand/Facing 反転可能な場合あり
//          System Panel は flip不可が多く、mirror/rotate のみ許可
// ====================================================================
#nullable enable
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;

namespace RevitMCPAddin.Commands.CurtainOps
{
    public class FlipCurtainPanelOrientationCommand : IRevitCommandHandler
    {
        public string CommandName => "flip_curtain_panel_orientation";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Err("アクティブドキュメントがありません。");

            var p = (cmd.Params as JObject) ?? new JObject();

            // ---- Target
            Element elem = null;
            JToken vId, vUid;
            if (p.TryGetValue("elementId", out vId) && vId.Type == JTokenType.Integer)
                elem = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(vId.Value<int>()));
            else if (p.TryGetValue("uniqueId", out vUid) && vUid.Type == JTokenType.String)
                elem = doc.GetElement(vUid.Value<string>());
            if (elem == null) return Err("要素が見つかりません（elementId/uniqueId を確認）。");

            if (elem.Category == null || elem.Category.Id.IntValue() != (int)BuiltInCategory.OST_CurtainWallPanels)
                return Err("対象がカーテンウォールパネルではありません（categoryId != OST_CurtainWallPanels）。");

            var fi = elem as FamilyInstance; // loadable panel の場合のみ
            bool isFamilyPanel = fi != null;

            // ---- Params
            bool dryRun = p.Value<bool?>("dryRun") ?? false;
            var hand = p["hand"] as JObject;
            var facing = p["facing"] as JObject;

            var mirror = p["mirror"] as JObject;
            bool mirrorEnabled = mirror?.Value<bool?>("enabled") ?? false;
            string mirrorMode = mirror?.Value<string>("mode") ?? "host";
            double rotateDeg = p.Value<double?>("rotateDeg") ?? 0.0;

            // ---- Before
            bool handFlipped = isFamilyPanel ? fi.HandFlipped : false;
            bool facingFlipped = isFamilyPanel ? fi.FacingFlipped : false;
            bool mirrored = isFamilyPanel ? fi.Mirrored : false;
            double yawDeg = isFamilyPanel ? GetYawDeg(fi) : 0.0;

            var before = new { handFlipped, facingFlipped, mirrored, yawDeg };

            // ---- Decide new states
            bool newHand = handFlipped;
            bool newFacing = facingFlipped;
            bool willMirror = mirrorEnabled;
            double willRot = Math.Abs(rotateDeg) > 1e-9 ? rotateDeg : 0.0;

            if (hand != null)
            {
                if (!isFamilyPanel) return Err("このパネルは Hand の反転に対応していません（システムパネル）。");
                var mode = hand.Value<string>("mode") ?? "toggle";
                if (mode == "toggle") newHand = !newHand;
                else if (mode == "set")
                {
                    JToken hv;
                    if (!hand.TryGetValue("value", out hv) || hv.Type != JTokenType.Boolean)
                        return Err("hand.mode=set の場合は hand.value (bool) が必要です。");
                    newHand = hv.Value<bool>();
                }
                else return Err("hand.mode は toggle/set のみ対応です。");
            }

            if (facing != null)
            {
                if (!isFamilyPanel) return Err("このパネルは Facing の反転に対応していません（システムパネル）。");
                var mode = facing.Value<string>("mode") ?? "toggle";
                if (mode == "toggle") newFacing = !newFacing;
                else if (mode == "set")
                {
                    JToken fv;
                    if (!facing.TryGetValue("value", out fv) || fv.Type != JTokenType.Boolean)
                        return Err("facing.mode=set の場合は facing.value (bool) が必要です。");
                    newFacing = fv.Value<bool>();
                }
                else return Err("facing.mode は toggle/set のみ対応です。");
            }

            bool anyAction =
                (isFamilyPanel && hand != null && newHand != handFlipped) ||
                (isFamilyPanel && facing != null && newFacing != facingFlipped) ||
                willMirror || Math.Abs(willRot) > 1e-9;

            if (!anyAction)
            {
                return new
                {
                    ok = true,
                    elementId = elem.Id.IntValue(),
                    applied = new { hand = "none", facing = "none", mirror = "none", rotateDeg = 0.0 },
                    before,
                    after = before,
                    notes = new[] { "適用すべき変更がありませんでした（dryRun/入力を確認）。" }
                };
            }

            // ---- Apply
            if (!dryRun)
            {
                using (var t = new Transaction(doc, "[MCP] Flip Curtain Panel Orientation"))
                {
                    t.Start();

                    if (isFamilyPanel)
                    {
                        // flips
                        if (hand != null && newHand != fi.HandFlipped)
                        {
                            if (!fi.CanFlipHand) { t.RollBack(); return Err("このパネルは Hand の反転に対応していません。"); }
                            try { fi.flipHand(); } catch (Exception ex) { t.RollBack(); return Err($"Hand 反転に失敗: {ex.Message}"); }
                        }

                        if (facing != null && newFacing != fi.FacingFlipped)
                        {
                            if (!fi.CanFlipFacing) { t.RollBack(); return Err("このパネルは Facing の反転に対応していません。"); }
                            try { fi.flipFacing(); } catch (Exception ex) { t.RollBack(); return Err($"Facing 反転に失敗: {ex.Message}"); }
                        }
                    }
                    else
                    {
                        // システムパネル: flips は不可
                        if (hand != null || facing != null)
                        {
                            t.RollBack();
                            return Err("システムパネルは Hand/Facing 反転に非対応です（mirror/rotateDeg をご利用ください）。");
                        }
                    }

                    // mirror
                    if (willMirror)
                    {
                        try
                        {
                            var plane = BuildMirrorPlane(doc, elem, mirrorMode, mirror?["plane"] as JObject);
                            if (plane == null) { t.RollBack(); return Err("ミラー用の平面を構築できませんでした。"); }
                            ElementTransformUtils.MirrorElements(doc, new[] { elem.Id }, plane, false);
                        }
                        catch (Exception ex)
                        {
                            t.RollBack();
                            return Err($"Mirror に失敗: {ex.Message}");
                        }
                    }

                    // rotate
                    if (Math.Abs(willRot) > 1e-9)
                    {
                        try
                        {
                            var axis = BuildVerticalAxisThrough(elem);
                            ElementTransformUtils.RotateElement(doc, elem.Id, axis, DegToRad(willRot));
                        }
                        catch (Exception ex)
                        {
                            t.RollBack();
                            return Err($"回転に失敗: {ex.Message}");
                        }
                    }

                    t.Commit();
                }
            }

            // ---- After
            bool afterHand = isFamilyPanel ? (dryRun ? newHand : ((doc.GetElement(elem.Id) as FamilyInstance)?.HandFlipped ?? newHand)) : false;
            bool afterFacing = isFamilyPanel ? (dryRun ? newFacing : ((doc.GetElement(elem.Id) as FamilyInstance)?.FacingFlipped ?? newFacing)) : false;
            bool afterMirrored = dryRun ? (mirrored || willMirror) : ((doc.GetElement(elem.Id) as FamilyInstance)?.Mirrored ?? (mirrored || willMirror));
            double afterYawDeg = isFamilyPanel ? (dryRun ? yawDeg + willRot : GetYawDeg(doc.GetElement(elem.Id) as FamilyInstance)) : 0.0;

            var applied = new
            {
                hand = hand != null ? (hand.Value<string>("mode") ?? "toggle") : "none",
                facing = facing != null ? (facing.Value<string>("mode") ?? "toggle") : "none",
                mirror = willMirror ? mirrorMode : "none",
                rotateDeg = willRot
            };

            return new
            {
                ok = true,
                elementId = elem.Id.IntValue(),
                applied,
                before,
                after = new { handFlipped = afterHand, facingFlipped = afterFacing, mirrored = afterMirrored, yawDeg = afterYawDeg },
                notes = isFamilyPanel ? Array.Empty<string>() : new[] { "システムパネルは Hand/Facing 反転不可。mirror/rotateDeg を使用しました。" }
            };
        }

        // ---------------- Helpers ----------------
        private static object Err(string msg) => new { ok = false, msg };
        private static double DegToRad(double deg) => deg * Math.PI / 180.0;

        private static double GetYawDeg(FamilyInstance fi)
        {
            try
            {
                var dir = fi.FacingOrientation;
                var yaw = Math.Atan2(dir.Y, dir.X) * 180.0 / Math.PI;
                if (yaw < 0) yaw += 360.0;
                return yaw;
            }
            catch { return 0.0; }
        }

        private static Line BuildVerticalAxisThrough(Element e)
        {
            var lp = (e.Location as LocationPoint);
            var p = lp != null ? lp.Point : XYZ.Zero;
            return Line.CreateBound(p - XYZ.BasisZ, p + XYZ.BasisZ);
        }

        private static Plane BuildMirrorPlane(Document doc, Element e, string mode, JObject planeObj)
        {
            var lp = (e.Location as LocationPoint);
            var origin = lp != null ? lp.Point : XYZ.Zero;

            if (mode == "vertical") return Plane.CreateByNormalAndOrigin(XYZ.BasisY, origin);
            if (mode == "plane" && planeObj != null)
            {
                const double ft = 0.00328083989501312;
                var o = planeObj["originMm"] as JObject;
                var n = planeObj["normal"] as JObject;
                var oorigin = new XYZ(o.Value<double>("x") * ft, o.Value<double>("y") * ft, o.Value<double>("z") * ft);
                var nnorm = new XYZ(n.Value<double>("x"), n.Value<double>("y"), n.Value<double>("z")).Normalize();
                return Plane.CreateByNormalAndOrigin(nnorm, oorigin);
            }

            // host (カーテン壁/システム) の外向き法線を使うことは難しい場合があるため、既定は垂直面
            return Plane.CreateByNormalAndOrigin(XYZ.BasisY, origin);
        }
    }
}


