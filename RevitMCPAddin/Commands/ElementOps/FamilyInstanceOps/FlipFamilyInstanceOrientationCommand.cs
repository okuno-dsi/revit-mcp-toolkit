// ================================================================
// File : Commands/FamilyOps/FlipFamilyInstanceOrientationCommand.cs
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Purpose: FamilyInstance 全般の Hand/Facing 反転・ミラー・回転（dryRun対応）
// Notes  : Doors/Windows 以外の Loadable Family にも一括適用可能
// ================================================================
#nullable enable
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;

namespace RevitMCPAddin.Commands.FamilyOps
{
    public class FlipFamilyInstanceOrientationCommand : IRevitCommandHandler
    {
        public string CommandName => "flip_family_instance_orientation";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Err("アクティブドキュメントがありません。");

            var p = (cmd.Params as JObject) ?? new JObject();

            // ---- Target resolve
            Element elem = null;
            JToken vId, vUid;
            if (p.TryGetValue("elementId", out vId) && vId.Type == JTokenType.Integer)
                elem = doc.GetElement(new ElementId(vId.Value<int>()));
            else if (p.TryGetValue("uniqueId", out vUid) && vUid.Type == JTokenType.String)
                elem = doc.GetElement(vUid.Value<string>());
            if (elem == null) return Err("要素が見つかりません（elementId/uniqueId を確認）。");

            var fi = elem as FamilyInstance;
            if (fi == null) return Err("FamilyInstance ではありません（操作不可）。");

            // ---- Params
            bool dryRun = p.Value<bool?>("dryRun") ?? false;
            var hand = p["hand"] as JObject;
            var facing = p["facing"] as JObject;

            var mirror = p["mirror"] as JObject;
            bool mirrorEnabled = mirror?.Value<bool?>("enabled") ?? false;
            string mirrorMode = mirror?.Value<string>("mode") ?? "host"; // "host"|"plane"|"vertical"
            double rotateDeg = p.Value<double?>("rotateDeg") ?? 0.0;

            // ---- Before
            var before = new
            {
                handFlipped = fi.HandFlipped,
                facingFlipped = fi.FacingFlipped,
                mirrored = fi.Mirrored,
                yawDeg = GetYawDeg(fi)
            };

            // Will states
            bool newHand = before.handFlipped;
            bool newFacing = before.facingFlipped;
            bool willMirror = mirrorEnabled;
            double willRot = Math.Abs(rotateDeg) > 1e-9 ? rotateDeg : 0.0;

            // hand mode
            if (hand != null)
            {
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

            // facing mode
            if (facing != null)
            {
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

            // 何もすることがない（flip不可、mirror/rotate無し）防止
            bool anyAction =
                (hand != null && newHand != before.handFlipped) ||
                (facing != null && newFacing != before.facingFlipped) ||
                willMirror || Math.Abs(willRot) > 1e-9;

            if (!anyAction)
            {
                return new
                {
                    ok = true,
                    elementId = fi.Id.IntegerValue,
                    applied = new { hand = "none", facing = "none", mirror = "none", rotateDeg = 0.0 },
                    before,
                    after = before,
                    notes = new[] { "適用すべき変更がありませんでした（dryRun/入力を確認）。" }
                };
            }

            // ---- Apply
            if (!dryRun)
            {
                using (var t = new Transaction(doc, "[MCP] Flip FamilyInstance Orientation"))
                {
                    t.Start();

                    // flip hand
                    if (hand != null && newHand != fi.HandFlipped)
                    {
                        if (!fi.CanFlipHand) { t.RollBack(); return Err("この要素は Hand の反転に対応していません。"); }
                        try { fi.flipHand(); } catch (Exception ex) { t.RollBack(); return Err($"Hand 反転に失敗: {ex.Message}"); }
                    }

                    // flip facing
                    if (facing != null && newFacing != fi.FacingFlipped)
                    {
                        if (!fi.CanFlipFacing) { t.RollBack(); return Err("この要素は Facing の反転に対応していません。"); }
                        try { fi.flipFacing(); } catch (Exception ex) { t.RollBack(); return Err($"Facing 反転に失敗: {ex.Message}"); }
                    }

                    // mirror
                    if (willMirror)
                    {
                        try
                        {
                            var plane = BuildMirrorPlane(doc, fi, mirrorMode, mirror?["plane"] as JObject);
                            if (plane == null) { t.RollBack(); return Err("ミラー用の平面を構築できませんでした。"); }
                            ElementTransformUtils.MirrorElements(doc, new[] { fi.Id }, plane, false);
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
                            var axis = BuildVerticalAxisThrough(fi);
                            ElementTransformUtils.RotateElement(doc, fi.Id, axis, DegToRad(willRot));
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
            bool afterHand, afterFacing, afterMirrored;
            double afterYawDeg;

            if (!dryRun)
            {
                var fi2 = doc.GetElement(fi.Id) as FamilyInstance;
                afterHand = fi2?.HandFlipped ?? newHand;
                afterFacing = fi2?.FacingFlipped ?? newFacing;
                afterMirrored = fi2?.Mirrored ?? (before.mirrored || willMirror);
                afterYawDeg = fi2 != null ? GetYawDeg(fi2) : before.yawDeg + willRot;
            }
            else
            {
                afterHand = newHand;
                afterFacing = newFacing;
                afterMirrored = before.mirrored || willMirror;
                afterYawDeg = before.yawDeg + willRot;
            }

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
                elementId = fi.Id.IntegerValue,
                applied,
                before,
                after = new { handFlipped = afterHand, facingFlipped = afterFacing, mirrored = afterMirrored, yawDeg = afterYawDeg },
                notes = Array.Empty<string>()
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

        private static Line BuildVerticalAxisThrough(FamilyInstance fi)
        {
            var loc = fi.Location as LocationPoint;
            var p = loc != null ? loc.Point : XYZ.Zero;
            return Line.CreateBound(p - XYZ.BasisZ, p + XYZ.BasisZ);
        }

        private static Plane BuildDefaultVerticalPlane(XYZ origin)
            => Plane.CreateByNormalAndOrigin(XYZ.BasisY, origin);

        private static Plane BuildPlaneFromMm(JObject planeObj)
        {
            // mm → ft
            const double ft = 0.00328083989501312;
            var o = planeObj["originMm"] as JObject;
            var n = planeObj["normal"] as JObject;
            var oorigin = new XYZ(o.Value<double>("x") * ft, o.Value<double>("y") * ft, o.Value<double>("z") * ft);
            var nnorm = new XYZ(n.Value<double>("x"), n.Value<double>("y"), n.Value<double>("z")).Normalize();
            return Plane.CreateByNormalAndOrigin(nnorm, oorigin);
        }

        private static Plane BuildMirrorPlane(Document doc, FamilyInstance fi, string mode, JObject planeObj)
        {
            var loc = fi.Location as LocationPoint;
            var origin = loc != null ? loc.Point : XYZ.Zero;

            if (mode == "vertical") return Plane.CreateByNormalAndOrigin(XYZ.BasisY, origin);
            if (mode == "plane" && planeObj != null) return BuildPlaneFromMm(planeObj);

            // host-based fallback
            var wall = fi.Host as Wall;
            if (wall != null)
            {
                var wallNormal = wall.Orientation; // 外向き法線
                return Plane.CreateByNormalAndOrigin(wallNormal, origin);
            }
            // 非ホストなら既定の垂直平面
            return Plane.CreateByNormalAndOrigin(XYZ.BasisY, origin);
        }
    }
}
