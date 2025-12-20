// ================================================================
// File: Commands/DoorOps/FlipDoorOrientationCommand.cs
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Purpose: ドアの Hand/Facing 反転・任意ミラー・任意回転を安全に適用
// Notes  : dryRun対応。Door以外は明確にエラー。結果にbefore/afterを返却。
// ================================================================
#nullable enable
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core; // RevitLogger など（既存ユーティリティを想定）

namespace RevitMCPAddin.Commands.DoorOps
{
    public class FlipDoorOrientationCommand : IRevitCommandHandler
    {
        public string CommandName => "flip_door_orientation";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject?)cmd.Params ?? new JObject();

            // ---- 1) ターゲット特定
            Element? elem = null;
            if (p.TryGetValue("elementId", out var vId) && vId.Type == JTokenType.Integer)
            {
                elem = doc.GetElement(new ElementId(vId.Value<int>()));
            }
            else if (p.TryGetValue("uniqueId", out var vUid) && vUid.Type == JTokenType.String)
            {
                elem = doc.GetElement(vUid.Value<string>());
            }
            if (elem == null) return new { ok = false, msg = "要素が見つかりません（elementId/uniqueId を確認）。" };

            // ドア判定
            if (elem.Category == null || elem.Category.Id.IntegerValue != (int)BuiltInCategory.OST_Doors)
                return new { ok = false, msg = "対象がドアではありません（categoryId != OST_Doors）。" };

            if (!(elem is FamilyInstance fi))
                return new { ok = false, msg = "FamilyInstance ではありません（操作不可）。" };

            // ---- 2) 入力解析
            var dryRun = p.Value<bool?>("dryRun") ?? false;

            var hand = p["hand"] as JObject;
            var facing = p["facing"] as JObject;

            var mirror = p["mirror"] as JObject;
            bool mirrorEnabled = mirror?.Value<bool?>("enabled") ?? false;
            string mirrorMode = mirror?.Value<string>("mode") ?? "host"; // "host"|"plane"|"vertical"

            double rotateDeg = p.Value<double?>("rotateDeg") ?? 0.0;

            // ---- 3) 現状(before)の取得
            var before = new
            {
                handFlipped = fi.HandFlipped,
                facingFlipped = fi.FacingFlipped,
                mirrored = fi.Mirrored,
                yawDeg = GetYawDeg(fi)
            };

            // ---- 4) シミュレーション（dryRunでも使用）
            bool newHand = before.handFlipped;
            bool newFacing = before.facingFlipped;
            bool willMirror = false;
            double willRotateDeg = Math.Abs(rotateDeg) > 1e-9 ? rotateDeg : 0.0;

            if (hand != null)
            {
                var mode = hand.Value<string>("mode") ?? "toggle";
                if (mode == "toggle") newHand = !newHand;
                else if (mode == "set")
                {
                    if (!hand.TryGetValue("value", out var hv) || hv.Type != JTokenType.Boolean)
                        return new { ok = false, msg = "hand.mode=set の場合は hand.value (bool) が必要です。" };
                    newHand = hv.Value<bool>();
                }
                else return new { ok = false, msg = "hand.mode は toggle/set のみ対応です。" };
            }

            if (facing != null)
            {
                var mode = facing.Value<string>("mode") ?? "toggle";
                if (mode == "toggle") newFacing = !newFacing;
                else if (mode == "set")
                {
                    if (!facing.TryGetValue("value", out var fv) || fv.Type != JTokenType.Boolean)
                        return new { ok = false, msg = "facing.mode=set の場合は facing.value (bool) が必要です。" };
                    newFacing = fv.Value<bool>();
                }
                else return new { ok = false, msg = "facing.mode は toggle/set のみ対応です。" };
            }

            if (mirrorEnabled) willMirror = true;

            // ---- 5) 適用（トランザクション）
            if (!dryRun)
            {
                using (var t = new Transaction(doc, "[MCP] Flip Door Orientation"))
                {
                    t.Start();

                    // Hand
                    if (hand != null && newHand != fi.HandFlipped)
                    {
                        try { fi.flipHand(); } catch (Exception ex) { t.RollBack(); return Err($"Hand 反転に失敗: {ex.Message}"); }
                    }

                    // Facing
                    if (facing != null && newFacing != fi.FacingFlipped)
                    {
                        try { fi.flipFacing(); } catch (Exception ex) { t.RollBack(); return Err($"Facing 反転に失敗: {ex.Message}"); }
                    }

                    // Mirror（幾何学操作のため慎重に）
                    if (willMirror)
                    {
                        try
                        {
                            Plane? plane = BuildMirrorPlane(doc, fi, mirrorMode, mirror?["plane"] as JObject);
                            if (plane == null) { t.RollBack(); return Err("ミラー用の平面を構築できませんでした。"); }
                            ElementTransformUtils.MirrorElements(doc, new[] { fi.Id }, plane, false);
                        }
                        catch (Exception ex)
                        {
                            t.RollBack();
                            return Err($"Mirror に失敗: {ex.Message}");
                        }
                    }

                    // Rotate（Z軸まわり）
                    if (Math.Abs(willRotateDeg) > 1e-9)
                    {
                        try
                        {
                            var axis = BuildVerticalAxisThrough(fi);
                            ElementTransformUtils.RotateElement(doc, fi.Id, axis, DegToRad(willRotateDeg));
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

            // ---- 6) 適用後(after)の取得
            // ドキュメント更新後の要素を再取得（dryRun時はbefore相当を合成）
            bool afterHand, afterFacing, afterMirrored;
            double afterYawDeg;
            if (!dryRun)
            {
                var fi2 = doc.GetElement(fi.Id) as FamilyInstance;
                afterHand = fi2?.HandFlipped ?? newHand;
                afterFacing = fi2?.FacingFlipped ?? newFacing;
                afterMirrored = fi2?.Mirrored ?? (before.mirrored || willMirror);
                afterYawDeg = fi2 != null ? GetYawDeg(fi2) : before.yawDeg + willRotateDeg;
            }
            else
            {
                afterHand = newHand;
                afterFacing = newFacing;
                afterMirrored = before.mirrored || willMirror;
                afterYawDeg = before.yawDeg + willRotateDeg;
            }

            var applied = new
            {
                hand = hand != null ? (hand.Value<string>("mode") ?? "toggle") : "none",
                facing = facing != null ? (facing.Value<string>("mode") ?? "toggle") : "none",
                mirror = willMirror ? mirrorMode : "none",
                rotateDeg = willRotateDeg
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

        // ---- Helpers -----------------------------------------------------

        private static object Err(string msg) => new { ok = false, msg };

        private static double DegToRad(double deg) => deg * Math.PI / 180.0;

        private static double GetYawDeg(FamilyInstance fi)
        {
            // 平面ビュー前提の簡易Yaw（Z軸投影の向き）
            try
            {
                var dir = fi.FacingOrientation; // ホスト壁の表裏に依存
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

        private static Plane? BuildMirrorPlane(Document doc, FamilyInstance fi, string mode, JObject? planeObj)
        {
            // 垂直な鏡面を作る。ホスト壁があればその中心線を含む面を既定に。
            var loc = fi.Location as LocationPoint;
            var origin = loc != null ? loc.Point : XYZ.Zero;

            if (mode == "vertical")
            {
                // グローバルYを法線、原点はドア位置
                return Plane.CreateByNormalAndOrigin(XYZ.BasisY, origin);
            }

            if (mode == "plane" && planeObj != null)
            {
                var o = planeObj["originMm"] as JObject;
                var n = planeObj["normal"] as JObject;
                if (o != null && n != null)
                {
                    XYZ omm = new XYZ(o.Value<double>("x"), o.Value<double>("y"), o.Value<double>("z"));
                    // mm→ft（簡易換算）
                    var ft = 0.00328083989501312;
                    var oorigin = new XYZ(omm.X * ft, omm.Y * ft, omm.Z * ft);
                    var nnorm = new XYZ(n.Value<double>("x"), n.Value<double>("y"), n.Value<double>("z"));
                    if (nnorm.IsZeroLength()) return null;
                    return Plane.CreateByNormalAndOrigin(nnorm.Normalize(), oorigin);
                }
                return null;
            }

            // "host" 既定：ホスト壁の中心線を含み、垂直(Z)方向の平面
            var wall = fi.Host as Wall;
            if (wall == null)
            {
                // 壁が無い場合は vertical のフォールバック
                return Plane.CreateByNormalAndOrigin(XYZ.BasisY, origin);
            }

            var lc = (wall.Location as LocationCurve)?.Curve;
            if (lc == null) return Plane.CreateByNormalAndOrigin(XYZ.BasisY, origin);

            var tangent = (lc as Line)?.Direction ?? (lc.ComputeDerivatives(0.5, true).BasisX.Normalize());
            // 壁の外向き法線（Orientation）は中心線に垂直
            var wallNormal = wall.Orientation; // 長さ1の水平ベクトル
            // 鏡面の法線＝壁外向き法線（中心線を含む平面）
            return Plane.CreateByNormalAndOrigin(wallNormal, origin);
        }
    }
}
