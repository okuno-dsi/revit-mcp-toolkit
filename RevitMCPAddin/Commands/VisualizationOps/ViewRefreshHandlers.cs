// ================================================================
// File   : Commands/ViewOps/ViewRefreshHandlers.cs
// Purpose: Force UI redraw and/or document regenerate.
// Target : .NET Framework 4.8 / Revit 2023+
// Return : { ok, msg }
// ================================================================
#nullable enable
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;

namespace RevitMCPAddin.Commands.ViewOps
{
    internal static class ViewPick
    {
        public static bool TryGetUIView(UIDocument? uidoc, int? viewIdOpt, out UIView uiv, out View? v)
        {
            uiv = null!;
            v = null;
            if (uidoc == null) return false;

            if (viewIdOpt.HasValue)
            {
                foreach (var uv in uidoc.GetOpenUIViews())
                {
                    if (uv.ViewId.IntValue() == viewIdOpt.Value)
                    {
                        uiv = uv;
                        v = uidoc.Document.GetElement(uv.ViewId) as View;
                        return v != null;
                    }
                }
            }
            var uivs = uidoc.GetOpenUIViews();
            if (uivs == null || uivs.Count == 0) return false;
            uiv = uivs[0];
            v = uidoc.Document.GetElement(uiv.ViewId) as View;
            return v != null;
        }
    }

    // ---------------------- refresh_view ----------------------
    public sealed class RefreshViewHandler : IRevitCommandHandler
    {
        public string CommandName => "refresh_view";

        public object Execute(UIApplication uiapp, RequestCommand req)
        {
            try
            {
                var j = req.Params as JObject;
                int? viewIdOpt = j?["viewId"]?.ToObject<int?>();

                var uidoc = uiapp.ActiveUIDocument;
                if (uidoc == null) return new { ok = false, msg = "No active document" };

                // ※ RefreshActiveView は常に「アクティブUIビュー」を対象に動く
                //   viewId 指定は一応受けるが、ここでは無視してアクティブを更新
                uidoc.RefreshActiveView();
                return new { ok = true, msg = "view refreshed" };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // ------------------- regen_and_refresh -------------------
    public sealed class RegenAndRefreshHandler : IRevitCommandHandler
    {
        public string CommandName => "regen_and_refresh";

        public object Execute(UIApplication uiapp, RequestCommand req)
        {
            try
            {
                var j = req.Params as JObject;
                int? viewIdOpt = j?["viewId"]?.ToObject<int?>();

                var uidoc = uiapp.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (uidoc == null || doc == null) return new { ok = false, msg = "No active document" };

                // Revit 2024 では Regenerate がトランザクション外だと失敗するケースがあるため、必要なら Tx を張る。
                try
                {
                    if (!doc.IsModifiable)
                    {
                        using (var tx = new Transaction(doc, "Regenerate (no-op)"))
                        {
                            tx.Start();
                            try { TxnUtil.ConfigureProceedWithWarnings(tx); } catch { }
                            doc.Regenerate();
                            tx.Commit();
                        }
                    }
                    else
                    {
                        doc.Regenerate();
                    }
                }
                catch (Exception ex)
                {
                    return new { ok = false, msg = "Regenerate failed: " + ex.Message };
                }

                try { uidoc.RefreshActiveView(); } catch { /* ignore */ }
                return new { ok = true, msg = "document regenerated and view refreshed" };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }
}

