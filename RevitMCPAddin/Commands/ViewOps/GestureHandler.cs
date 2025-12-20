// ================================================================
// File   : Commands/ViewOps/GestureHandlers.cs
// Purpose: Gesture RPC handlers (zoom/orbit/pan) that delegate to
//          view_* handlers. Includes enable switches.
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Return : { ok, appliedToViewId, msg }
// ================================================================
#nullable enable
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;

namespace RevitMCPAddin.Commands.ViewOps
{
    /// <summary>
    /// Feature flags for gesture routes (process-wide).
    /// </summary>
    internal static class GestureFeatureFlags
    {
        public static volatile bool ZoomEnabled = true;
        public static volatile bool OrbitEnabled = true;
        public static volatile bool PanEnabled = true;
    }

    // ------------------------------------------------------------
    // Compatibility: set_gesture_zoom_enabled { enabled: bool, note?: string }
    // ------------------------------------------------------------
    public sealed class SetGestureZoomEnabledHandler : IRevitCommandHandler
    {
        public string CommandName => "set_gesture_zoom_enabled";

        public object Execute(UIApplication uiapp, RequestCommand req)
        {
            try
            {
                var j = req.Params as JObject;
                bool enabled = j?["enabled"]?.ToObject<bool>() ?? false;
                GestureFeatureFlags.ZoomEnabled = enabled;
                return new { ok = true, msg = $"gesture zoom {(enabled ? "enabled" : "disabled")}" };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // ------------------------------------------------------------
    // Extension: set_gesture_enabled { kind:"zoom|orbit|pan"|"all", enabled:bool }
    // ------------------------------------------------------------
    public sealed class SetGestureEnabledHandler : IRevitCommandHandler
    {
        public string CommandName => "set_gesture_enabled";

        public object Execute(UIApplication uiapp, RequestCommand req)
        {
            try
            {
                var j = req.Params as JObject;
                string kind = (j?["kind"]?.ToObject<string>() ?? "all").ToLowerInvariant();
                bool enabled = j?["enabled"]?.ToObject<bool>() ?? false;

                switch (kind)
                {
                    case "zoom": GestureFeatureFlags.ZoomEnabled = enabled; break;
                    case "orbit": GestureFeatureFlags.OrbitEnabled = enabled; break;
                    case "pan": GestureFeatureFlags.PanEnabled = enabled; break;
                    default:
                        GestureFeatureFlags.ZoomEnabled = enabled;
                        GestureFeatureFlags.OrbitEnabled = enabled;
                        GestureFeatureFlags.PanEnabled = enabled;
                        break;
                }
                return new { ok = true, msg = $"gesture {kind} {(enabled ? "enabled" : "disabled")}" };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // ------------------------------------------------------------
    // gesture_zoom { delta: number, viewId?: int }  (positive=in)
    // Delegates to view_zoom { factor }
    // ------------------------------------------------------------
    public sealed class GestureZoomHandler : IRevitCommandHandler
    {
        public string CommandName => "gesture_zoom";

        public object Execute(UIApplication uiapp, RequestCommand req)
        {
            try
            {
                if (!GestureFeatureFlags.ZoomEnabled)
                    return new { ok = false, appliedToViewId = (int?)null, msg = "gesture zoom disabled" };

                var j = req.Params as JObject;
                double delta = j?["delta"]?.ToObject<double>() ?? 0.0;
                int? viewIdOpt = j?["viewId"]?.ToObject<int?>();

                if (Math.Abs(delta) < 1e-6)
                    return new { ok = false, appliedToViewId = (int?)null, msg = "delta is zero" };

                // map delta -> factor (exponential scale)
                double factor = Math.Exp(delta);

                // delegate to view_zoom
                var subReq = new RequestCommand
                {
                    Method = "view_zoom",
                    Params = JObject.FromObject(new { factor = factor, viewId = viewIdOpt })
                };
                var result = new ViewZoomHandler().Execute(uiapp, subReq) as dynamic;

                return new
                {
                    ok = (bool)(result?.ok ?? false),
                    appliedToViewId = (int?)result?.viewId,
                    msg = (string)(result?.msg ?? "")
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, appliedToViewId = (int?)null, msg = ex.Message };
            }
        }
    }

    // ------------------------------------------------------------
    // gesture_orbit { dyaw:number, dpitch:number, viewId?:int }
    // Delegates to view_orbit (dyaw/dpitch are radians)
    // ------------------------------------------------------------
    public sealed class GestureOrbitHandler : IRevitCommandHandler
    {
        public string CommandName => "gesture_orbit";

        public object Execute(UIApplication uiapp, RequestCommand req)
        {
            try
            {
                if (!GestureFeatureFlags.OrbitEnabled)
                    return new { ok = false, appliedToViewId = (int?)null, msg = "gesture orbit disabled" };

                var j = req.Params as JObject;
                double dyaw = j?["dyaw"]?.ToObject<double>() ?? 0.0;
                double dpitch = j?["dpitch"]?.ToObject<double>() ?? 0.0;
                int? viewIdOpt = j?["viewId"]?.ToObject<int?>();

                if (Math.Abs(dyaw) < 1e-9 && Math.Abs(dpitch) < 1e-9)
                    return new { ok = true, appliedToViewId = (int?)null, msg = "no-op" };

                var subReq = new RequestCommand
                {
                    Method = "view_orbit",
                    Params = JObject.FromObject(new { dyaw = dyaw, dpitch = dpitch, viewId = viewIdOpt })
                };
                var result = new ViewOrbitHandler().Execute(uiapp, subReq) as dynamic;

                return new
                {
                    ok = (bool)(result?.ok ?? false),
                    appliedToViewId = (int?)result?.viewId,
                    msg = (string)(result?.msg ?? "")
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, appliedToViewId = (int?)null, msg = ex.Message };
            }
        }
    }

    // ------------------------------------------------------------
    // gesture_pan { dx:number, dy:number, viewId?:int }
    // Delegates to view_pan (dx,dy are normalized screen-space)
    // ------------------------------------------------------------
    public sealed class GesturePanHandler : IRevitCommandHandler
    {
        public string CommandName => "gesture_pan";

        public object Execute(UIApplication uiapp, RequestCommand req)
        {
            try
            {
                if (!GestureFeatureFlags.PanEnabled)
                    return new { ok = false, appliedToViewId = (int?)null, msg = "gesture pan disabled" };

                var j = req.Params as JObject;
                double dx = j?["dx"]?.ToObject<double>() ?? 0.0;
                double dy = j?["dy"]?.ToObject<double>() ?? 0.0;
                int? viewIdOpt = j?["viewId"]?.ToObject<int?>();

                if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9)
                    return new { ok = true, appliedToViewId = (int?)null, msg = "no-op" };

                var subReq = new RequestCommand
                {
                    Method = "view_pan",
                    Params = JObject.FromObject(new { dx = dx, dy = dy, viewId = viewIdOpt })
                };
                var result = new ViewPanHandler().Execute(uiapp, subReq) as dynamic;

                return new
                {
                    ok = (bool)(result?.ok ?? false),
                    appliedToViewId = (int?)result?.viewId,
                    msg = (string)(result?.msg ?? "")
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, appliedToViewId = (int?)null, msg = ex.Message };
            }
        }
    }
}
