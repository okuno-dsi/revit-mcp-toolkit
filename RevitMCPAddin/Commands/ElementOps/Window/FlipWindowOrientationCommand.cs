// ================================================================
// File: Commands/WindowOps/FlipWindowOrientationCommand.cs
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Purpose: 窓(Window: OST_Windows) の Hand/Facing 反転・任意ミラー・任意回転
// Notes  : dryRun対応。Window以外/不可要素はわかりやすいエラーを返す。
// ================================================================
#nullable enable
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;

namespace RevitMCPAddin.Commands.WindowOps
{
    public class FlipWindowOrientationCommand : IRevitCommandHandler
    {
        public string CommandName => "flip_window_orientation";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Err("アクティブドキュメントがありません。");

            var p = (cmd.Params as JObject) ?? new JObject();

            // ---- 1) ターゲット特定
            Element? elem = null;
            if (p.TryGetValue("elementId", out var vId) && vId.Type == JTokenType.Integer)
                elem = doc.GetElement(new ElementId(vId.Value<int>()));
            else if (p.TryGetValue("uniqueId", out var vUid) && vUid.Type == JTokenType.String)
                elem = doc.GetElement(vUid.Value<string>());

            if (elem == null) return Err("要素が見つかりません（elementId/uniqueId を確認）。");

            // 窓カテゴリ判定 (OST_Windows)
            if (elem.Category == null || elem.Category.Id.IntegerValue != (int)BuiltInCategory.OST_Windows)
                return Err("対象が窓ではありません（categoryId != OST_Windows）。");

            var fi = elem as FamilyInstance;
            if (fi == null)
                return Err("FamilyInstance ではありません（操作不可）。");

            // 一部のホスト/族では flip が無効なケースがある
            if (!fi.CanFlipHand && !fi.CanFlipFacing)
            {
                // カーテンパネル窓など
                return Err("この窓は Hand/Facing の反転に対応していません（族/ホスト制約）。");
            }

            // ---- 2) 入力解析
            var dryRun = p.Value<bool?>("dryRun") ?? false;
            var hand = p["hand"] as JObject;    // { "mode": "toggle" | "set", "value": bool }
            var facing = p["facing"] as JObject;    // 同上

            var mirror = p["mirror"] as JObject;    // { "enabled": bool, "mode": "host"|"plane"|"vertical", "plane": {...} }
            bool mirrorEnabled = mirror?.Value<bool?>("enabled") ?? false;
            string mirrorMode = mirror?.Value<string>("mode") ?? "host";

            double rotateDeg = p.Value<double?>("rotateDeg") ?? 0.0;

            // ---- 3) 事前状態(before)
            var before = new
            {
                handFlipped = fi.HandFlipped,
                facingFlipped = fi.FacingFlipped,
                mirrored = fi.Mirrored,
                yawDeg = GetYawDeg(fi)
            };

            // ---- 4) シミュレーション
            bool newHand = before.handFlipped;
            bool newFacing = before.facingFlipped;
            bool willMirror = mirrorEnabled;
            double willRot = Math.Abs(rotateDeg) > 1e-9 ? rotateDeg : 0.0;

            if (hand != null)
            {
                var mode = hand.Value<string>("mode") ?? "toggle";
                if (mode == "toggle") newHand = !newHand;
                else if (mode == "set")
                {
                    if (!hand.TryGetValue("value", out var hv) || hv.Type != JTokenType.Boolean)
                        return Err("hand.mode=set の場合は hand.value (bool) が必要です。");
                    newHand = hv.Value<bool>();
                }
                else return Err("hand.mode は toggle/set のみ対応です。");
            }

            if (facing != null)
            {
                var mode = facing.Value<string>("mode") ?? "toggle";
                if (mode == "toggle") newFacing = !newFacing;
                else if (mode == "set")
                {
                    if (!facing.TryGetValue("value", out var fv) || fv.Type != JTokenType.Boolean)
                        return Err("facing.mode=set の場合は facing.value (bool) が必要です。");
                    newFacing = fv.Value<bool>();
                }
                else return Err("facing.mode は toggle/set のみ対応です。");
            }

            // ---- 5) 適用（トランザクション）
            if (!dryRun)
            {
                using var t = new Transaction(doc, "[MCP] Flip Window Orientation");
                t.Start();

                // Hand flip
                if (hand != null && newHand != fi.HandFlipped)
                {
                    if (!fi.CanFlipHand) { t.RollBack(); return Err("この窓は Hand の反転に対応していません。"); }
                    try { fi.flipHand(); } catch (Exception ex) { t.RollBack(); return Err($"Hand 反転に失敗: {ex.Message}"); }
                }

                // Facing flip
                if (facing != null && newFacing != fi.FacingFlipped)
                {
                    if (!fi.CanFlipFacing) { t.RollBack(); return Err("この窓は Facing の反転に対応していません。"); }
                    try { fi.flipFacing(); } catch (Exception ex) { t.RollBack(); return Err($"Facing 反転に失敗: {ex.Message}"); }
                }

                // Mirror
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

                // Rotate（Z軸）
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

            // ---- 6) 事後状態(after)
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

        // --------------------------- Helpers -------------------------------
        private static object Err(string msg) => new { ok = false, msg };
        private static double DegToRad(double deg) => deg * Math.PI / 180.0;

        private static double GetYawDeg(FamilyInstance fi)
        {
            try
            {
                var dir = fi.FacingOrientation; // XY平面上での向き
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
            // 既定: ホスト壁の外向き法線を法線とする垂直面（窓位置を通る）
            var loc = fi.Location as LocationPoint;
            var origin = loc != null ? loc.Point : XYZ.Zero;

            if (mode == "vertical")
                return Plane.CreateByNormalAndOrigin(XYZ.BasisY, origin);

            if (mode == "plane" && planeObj != null)
            {
                var o = planeObj["originMm"] as JObject;
                var n = planeObj["normal"] as JObject;
                if (o != null && n != null)
                {
                    // mm→ft
                    const double ft = 0.00328083989501312;
                    var oorigin = new XYZ((o.Value<double>("x")) * ft, (o.Value<double>("y")) * ft, (o.Value<double>("z")) * ft);
                    var nnorm = new XYZ(n.Value<double>("x"), n.Value<double>("y"), n.Value<double>("z"));
                    if (nnorm.IsZeroLength()) return null;
                    return Plane.CreateByNormalAndOrigin(nnorm.Normalize(), oorigin);
                }
                return null;
            }

            // "host": ホスト壁があれば、その外向き法線を法線とする垂直平面を採用
            var wall = fi.Host as Wall;
            if (wall == null) return Plane.CreateByNormalAndOrigin(XYZ.BasisY, origin);

            var wallNormal = wall.Orientation; // 水平正規化ベクトル
            return Plane.CreateByNormalAndOrigin(wallNormal, origin);
        }
    }
}
