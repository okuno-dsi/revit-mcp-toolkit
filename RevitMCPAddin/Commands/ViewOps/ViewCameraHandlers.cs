// ================================================================
// File   : Commands/ViewOps/ViewCameraHandlers.cs
// Purpose: Unified handlers for view operations (zoom, orbit, pan, reset, fit, zoom_to_element)
// Target : .NET Framework 4.8 / Revit 2023+
// Notes  : Horizon lock: keep Up=(0,0,1) for zero roll; turntable-style orbit.
// ================================================================
#nullable enable
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RevitMCPAddin.Commands.ViewOps
{
    // ================================================================
    // Global camera constraints (process-wide)
    // ================================================================
    internal static class CameraConstraints
    {
        // 地平線水平を既定で有効
        public static bool HorizonLockEnabled = true;

        // ピッチ上限（度）
        public static double PitchLimitDeg = 89.0;
        public static double PitchLimitRad => Math.PI * PitchLimitDeg / 180.0;

        public static readonly XYZ WorldUp = new XYZ(0, 0, 1);
    }

    // ================================================================
    // Common Result DTO
    // ================================================================
    internal sealed class ViewCommandResult
    {
        public bool ok { get; set; }
        public int? viewId { get; set; }
        public string msg { get; set; } = "";
        public static ViewCommandResult From(bool ok, int? vid, string msg)
            => new ViewCommandResult { ok = ok, viewId = vid, msg = msg };
        public static ViewCommandResult Fail(string msg)
            => new ViewCommandResult { ok = false, viewId = null, msg = msg };
    }

    // ================================================================
    // Helper utilities
    // ================================================================
    internal static class ViewHelpers
    {
        public static bool TryGetUIView(UIDocument uidoc, int? viewIdOpt, out UIView uiv, out View? view)
        {
            uiv = null!;
            view = null;
            if (uidoc == null) return false;
            if (viewIdOpt.HasValue)
            {
                foreach (var v in uidoc.GetOpenUIViews())
                {
                    if (v.ViewId.IntValue() == viewIdOpt.Value)
                    {
                        uiv = v;
                        view = uidoc.Document.GetElement(v.ViewId) as View;
                        return view != null;
                    }
                }
            }
            var views = uidoc.GetOpenUIViews();
            if (views == null || views.Count == 0) return false;
            uiv = views[0];
            view = uidoc.Document.GetElement(uiv.ViewId) as View;
            return view != null;
        }

        // GetZoomCorners reflection (handles API differences)
        public static bool TryGetZoomCorners(UIView uiv, out XYZ min, out XYZ max)
        {
            min = max = XYZ.Zero;
            var m1 = typeof(UIView).GetMethod("GetZoomCorners", new[] { typeof(XYZ).MakeByRefType(), typeof(XYZ).MakeByRefType() });
            if (m1 != null)
            {
                object[] args = new object[] { null!, null! };
                m1.Invoke(uiv, args);
                var a = (XYZ)args[0]; var b = (XYZ)args[1];
                min = new XYZ(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
                max = new XYZ(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
                return true;
            }
            var m2 = typeof(UIView).GetMethod("GetZoomCorners", Type.EmptyTypes);
            if (m2 != null && typeof(System.Collections.IEnumerable).IsAssignableFrom(m2.ReturnType))
            {
                var listObj = m2.Invoke(uiv, null);
                var list = new List<XYZ>();
                foreach (var it in (System.Collections.IEnumerable)listObj!)
                    if (it is XYZ xyz) list.Add(xyz);
                if (list.Count >= 2)
                {
                    var a = list[0]; var b = list[1];
                    min = new XYZ(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
                    max = new XYZ(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
                    return true;
                }
            }
            return false;
        }

        public static double Clamp(double v, double lo, double hi) => (v < lo) ? lo : (v > hi) ? hi : v;

        public static XYZ NormalizeOrDefault(XYZ v, XYZ fallback)
        {
            double len = v.GetLength();
            return (len > 1e-12) ? (v / len) : fallback;
        }
    }

    // ================================================================
    // view_zoom
    // ================================================================
    public sealed class ViewZoomHandler : IRevitCommandHandler
    {
        public string CommandName => "view_zoom";

        public object Execute(UIApplication uiapp, RequestCommand req)
        {
            var factor = (req.Params as JObject)?["factor"]?.ToObject<double>() ?? 0.0;
            var viewIdOpt = (req.Params as JObject)?["viewId"]?.ToObject<int?>();

            if (factor <= 0) return ViewCommandResult.Fail("factor must be >0");
            if (!ViewHelpers.TryGetUIView(uiapp.ActiveUIDocument, viewIdOpt, out var uiv, out var v))
                return ViewCommandResult.Fail("No active view");

            try
            {
                if (v is View3D v3 && v3.IsPerspective)
                {
                    using (var t = new Transaction(v.Document, "Zoom (perspective)"))
                    {
                        t.Start();
                        var o = v3.GetOrientation();
                        XYZ fwd = ViewHelpers.NormalizeOrDefault(o.ForwardDirection, new XYZ(1, 0, 0));
                        var bb = v3.get_BoundingBox(null);
                        double diag = (bb?.Max - bb?.Min)?.GetLength() ?? 10.0;
                        double step = Math.Log(Math.Max(1e-6, factor)) * (diag * 0.05);
                        XYZ newEye = o.EyePosition + fwd * step;
                        v3.SetOrientation(new ViewOrientation3D(newEye, o.UpDirection, o.ForwardDirection));
                        t.Commit();
                        uiapp.ActiveUIDocument?.RefreshActiveView();
                    }
                }
                else
                {
                    if (ViewHelpers.TryGetZoomCorners(uiv, out var min, out var max))
                    {
                        XYZ center = (min + max) * 0.5;
                        XYZ half = (max - min) * 0.5 / factor;
                        uiv.ZoomAndCenterRectangle(center - half, center + half);
                    }
                    else
                    {
                        uiv.Zoom(factor);
                    }
                }
                return ViewCommandResult.From(true, uiv.ViewId.IntValue(), $"zoom x{factor:0.###}");
            }
            catch (Exception ex) { return ViewCommandResult.Fail(ex.Message); }
        }
    }

    // ================================================================
    // view_orbit  (HORIZON LOCK対応)
    // ================================================================
    public sealed class ViewOrbitHandler : IRevitCommandHandler
    {
        public string CommandName => "view_orbit";

        public object Execute(UIApplication uiapp, RequestCommand req)
        {
            var j = req.Params as JObject;
            double dyaw = j?["dyaw"]?.ToObject<double>() ?? 0.0;
            double dpitch = j?["dpitch"]?.ToObject<double>() ?? 0.0;
            bool horizonLock = j?["horizonLock"]?.ToObject<bool?>() ?? CameraConstraints.HorizonLockEnabled;
            var viewIdOpt = j?["viewId"]?.ToObject<int?>();

            if (!ViewHelpers.TryGetUIView(uiapp.ActiveUIDocument, viewIdOpt, out var uiv, out var v))
                return ViewCommandResult.Fail("No active view");
            if (!(v is View3D v3)) return ViewCommandResult.Fail("orbit is 3D only");

            try
            {
                using (var t = new Transaction(v.Document, "Orbit"))
                {
                    t.Start();
                    var o = v3.GetOrientation();

                    if (!horizonLock)
                    {
                        // 旧式：Up/Right 回りで自由回転（ロール発生あり）
                        XYZ f = ViewHelpers.NormalizeOrDefault(o.ForwardDirection, new XYZ(1, 0, 0));
                        XYZ up = ViewHelpers.NormalizeOrDefault(o.UpDirection, CameraConstraints.WorldUp);
                        XYZ right = f.CrossProduct(up).Normalize();

                        XYZ f1 = RotateAroundAxis(f, up, dyaw).Normalize();
                        XYZ up1 = RotateAroundAxis(up, right, dpitch).Normalize();
                        XYZ right1 = f1.CrossProduct(up1).Normalize();
                        up1 = right1.CrossProduct(f1).Normalize();

                        v3.SetOrientation(new ViewOrientation3D(o.EyePosition, up1, f1));
                    }
                    else
                    {
                        // 地平線固定（ロール0）ターンテーブル:
                        // 1) 現在の forward から azimuth/pitch を抽出
                        // 地平線固定（ロール0）ターンテーブル
                        XYZ upWorld = CameraConstraints.WorldUp;

                        // 現在の forward から方位角/仰角を取り、増分適用＋クランプ
                        XYZ f = ViewHelpers.NormalizeOrDefault(o.ForwardDirection, new XYZ(1, 0, 0));
                        XYZ fXY = new XYZ(f.X, f.Y, 0.0);
                        double az = Math.Atan2(fXY.Y, fXY.X);                                  // -pi..pi
                        double pitch = Math.Asin(Math.Max(-1.0, Math.Min(1.0, f.Z)));           // -pi/2..pi/2
                        az += dyaw;
                        double lim = CameraConstraints.PitchLimitRad;
                        pitch = ViewHelpers.Clamp(pitch + dpitch, -lim, +lim);

                        // 新しい forward を再構成（方位×仰角）
                        double cosP = Math.Cos(pitch), sinP = Math.Sin(pitch);
                        XYZ dirOnXY = new XYZ(Math.Cos(az), Math.Sin(az), 0.0);                 // XY unit
                        XYZ fNew = (dirOnXY * cosP + upWorld * sinP);
                        fNew = ViewHelpers.NormalizeOrDefault(fNew, new XYZ(1, 0, 0));

                        // ★ Up は「WorldUp を Forward に直交投影」→ ロール0・直交保証
                        // Uproj = U - (U·F)F
                        XYZ uProj = upWorld - (upWorld.DotProduct(fNew)) * fNew;
                        double uLen = uProj.GetLength();

                        // FがほぼWorldUpに平行で投影が消える場合のフォールバック
                        if (uLen < 1e-9)
                        {
                            // Fと直交する既知の軸を1つ見つける（例: X軸 or Y軸）
                            XYZ tmp = Math.Abs(fNew.Z) < 0.99 ? new XYZ(0, 0, 1) : new XYZ(1, 0, 0);
                            uProj = tmp - (tmp.DotProduct(fNew)) * fNew;
                            uLen = uProj.GetLength();
                        }
                        XYZ uNew = uProj / uLen;                        // Up ⟂ Forward, roll=0
                                                                        // Rightは直交再構成（念のため）
                        XYZ rNew = fNew.CrossProduct(uNew).Normalize();
                        uNew = rNew.CrossProduct(fNew).Normalize();     // 数値直交性をさらに安定化

                        v3.SetOrientation(new ViewOrientation3D(o.EyePosition, uNew, fNew));
                    }

                    t.Commit();
                    uiapp.ActiveUIDocument?.RefreshActiveView();
                }
                return ViewCommandResult.From(true, uiv.ViewId.IntValue(), $"orbit yaw={dyaw:0.###}, pitch={dpitch:0.###}, horizonLock={(horizonLock ? "on" : "off")}");
            }
            catch (Exception ex) { return ViewCommandResult.Fail(ex.Message); }
        }

        private static XYZ RotateAroundAxis(XYZ v, XYZ axis, double angleRad)
        {
            axis = axis.Normalize();
            double c = Math.Cos(angleRad), s = Math.Sin(angleRad);
            double u = axis.X, vv = axis.Y, w = axis.Z;
            return new XYZ(
                (u * u + (1 - u * u) * c) * v.X + (u * vv * (1 - c) - w * s) * v.Y + (u * w * (1 - c) + vv * s) * v.Z,
                (u * vv * (1 - c) + w * s) * v.X + (vv * vv + (1 - vv * vv) * c) * v.Y + (vv * w * (1 - c) - u * s) * v.Z,
                (u * w * (1 - c) - vv * s) * v.X + (vv * w * (1 - c) + u * s) * v.Y + (w * w + (1 - w * w) * c) * v.Z
            );
        }
    }

    // ================================================================
    // view_pan
    // ================================================================
    public sealed class ViewPanHandler : IRevitCommandHandler
    {
        public string CommandName => "view_pan";

        public object Execute(UIApplication uiapp, RequestCommand req)
        {
            var j = req.Params as JObject;
            double dx = j?["dx"]?.ToObject<double>() ?? 0.0;
            double dy = j?["dy"]?.ToObject<double>() ?? 0.0;
            var viewIdOpt = j?["viewId"]?.ToObject<int?>();

            if (!ViewHelpers.TryGetUIView(uiapp.ActiveUIDocument, viewIdOpt, out var uiv, out var v))
                return ViewCommandResult.Fail("No active view");

            try
            {
                if (v is View3D v3 && v3.IsPerspective)
                {
                    using (var t = new Transaction(v.Document, "Pan (perspective)"))
                    {
                        t.Start();
                        var o = v3.GetOrientation();
                        XYZ f = ViewHelpers.NormalizeOrDefault(o.ForwardDirection, new XYZ(1, 0, 0));
                        // パンは水平を崩さないため、UpはWorldUp基準のほうが気持ちよい
                        XYZ up = CameraConstraints.WorldUp;
                        XYZ right = f.CrossProduct(up).Normalize();

                        var bb = v3.get_BoundingBox(null);
                        double diag = (bb?.Max - bb?.Min)?.GetLength() ?? 10.0;
                        double scale = diag * 0.15;
                        XYZ delta = right * (dx * scale) + up * (dy * scale);

                        v3.SetOrientation(new ViewOrientation3D(o.EyePosition + delta, o.UpDirection, o.ForwardDirection));
                        t.Commit();
                        uiapp.ActiveUIDocument?.RefreshActiveView();
                    }
                }
                else
                {
                    if (ViewHelpers.TryGetZoomCorners(uiv, out var min, out var max))
                    {
                        XYZ size = max - min;
                        XYZ shift = new XYZ(size.X * dx, size.Y * dy, 0);
                        uiv.ZoomAndCenterRectangle(min + shift, max + shift);
                    }
                    else
                    {
                        return ViewCommandResult.Fail("pan not supported in this environment");
                    }
                }
                return ViewCommandResult.From(true, uiv.ViewId.IntValue(), $"pan dx={dx},dy={dy}");
            }
            catch (Exception ex) { return ViewCommandResult.Fail(ex.Message); }
        }
    }

    // ================================================================
    // view_reset_origin
    // ================================================================
    public sealed class ViewResetOriginHandler : IRevitCommandHandler
    {
        public string CommandName => "view_reset_origin";

        public object Execute(UIApplication uiapp, RequestCommand req)
        {
            var viewIdOpt = (req.Params as JObject)?["viewId"]?.ToObject<int?>();

            if (!ViewHelpers.TryGetUIView(uiapp.ActiveUIDocument, viewIdOpt, out var uiv, out var v))
                return ViewCommandResult.Fail("No active view");

            try
            {
                if (v is View3D v3)
                {
                    using (var t = new Transaction(v.Document, "Reset Origin"))
                    {
                        t.Start();
                        var o = v3.GetOrientation();
                        XYZ f = ViewHelpers.NormalizeOrDefault(o.ForwardDirection, new XYZ(1, 0, 0));
                        double dist = 10.0;
                        XYZ newEye = XYZ.Zero - f * dist;
                        v3.SetOrientation(new ViewOrientation3D(newEye, o.UpDirection, f));
                        t.Commit();
                        uiapp.ActiveUIDocument?.RefreshActiveView();
                    }
                }
                else
                {
                    if (ViewHelpers.TryGetZoomCorners(uiv, out var min, out var max))
                    {
                        XYZ size = max - min;
                        XYZ half = size * 0.5;
                        uiv.ZoomAndCenterRectangle(XYZ.Zero - half, XYZ.Zero + half);
                    }
                    else uiv.ZoomToFit();
                }
                return ViewCommandResult.From(true, uiv.ViewId.IntValue(), "reset to origin");
            }
            catch (Exception ex) { return ViewCommandResult.Fail(ex.Message); }
        }
    }

    // ================================================================
    // view_fit
    // ================================================================
    public sealed class ViewFitHandler : IRevitCommandHandler
    {
        public string CommandName => "view_fit";

        public object Execute(UIApplication uiapp, RequestCommand req)
        {
            var viewIdOpt = (req.Params as JObject)?["viewId"]?.ToObject<int?>();
            if (!ViewHelpers.TryGetUIView(uiapp.ActiveUIDocument, viewIdOpt, out var uiv, out var v))
                return ViewCommandResult.Fail("No active view");
            try
            {
                uiv.ZoomToFit();
                return ViewCommandResult.From(true, uiv.ViewId.IntValue(), "fit to screen");
            }
            catch (Exception ex) { return ViewCommandResult.Fail(ex.Message); }
        }
    }

    // ================================================================
    // view_zoom_to_element
    // ================================================================
    public sealed class ViewZoomToElementHandler : IRevitCommandHandler
    {
        public string CommandName => "view_zoom_to_element";

        public object Execute(UIApplication uiapp, RequestCommand req)
        {
            var j = req.Params as JObject;
            int elemId = j?["elementId"]?.ToObject<int>() ?? -1;
            double padding = j?["padding"]?.ToObject<double>() ?? 0.1;
            var viewIdOpt = j?["viewId"]?.ToObject<int?>();

            if (elemId < 0) return ViewCommandResult.Fail("elementId missing");
            if (!ViewHelpers.TryGetUIView(uiapp.ActiveUIDocument, viewIdOpt, out var uiv, out var v))
                return ViewCommandResult.Fail("No active view");

            try
            {
                var elem = v.Document.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(elemId));
                if (elem == null) return ViewCommandResult.Fail($"element not found {elemId}");
                var bb = elem.get_BoundingBox(v) ?? elem.get_BoundingBox(null);
                if (bb == null) return ViewCommandResult.Fail("element has no bounding box");

                XYZ min = bb.Min; XYZ max = bb.Max;
                XYZ size = max - min; XYZ pad = size * padding;
                min -= pad; max += pad;

                if (v is View3D v3 && v3.IsPerspective)
                {
                    using (var t = new Transaction(v.Document, "ZoomToElement"))
                    {
                        t.Start();
                        XYZ center = (min + max) * 0.5;
                        XYZ diag = max - min;
                        double dist = diag.GetLength() * 0.9;
                        var o = v3.GetOrientation();
                        XYZ f = ViewHelpers.NormalizeOrDefault(o.ForwardDirection, new XYZ(1, 0, 0));
                        XYZ newEye = center - f * Math.Max(1e-3, dist);
                        v3.SetOrientation(new ViewOrientation3D(newEye, o.UpDirection, f));
                        t.Commit();
                        uiapp.ActiveUIDocument?.RefreshActiveView();
                    }
                }
                else
                {
                    uiv.ZoomAndCenterRectangle(min, max);
                }
                return ViewCommandResult.From(true, uiv.ViewId.IntValue(), $"zoom to element {elemId}");
            }
            catch (Exception ex) { return ViewCommandResult.Fail(ex.Message); }
        }
    }
}


