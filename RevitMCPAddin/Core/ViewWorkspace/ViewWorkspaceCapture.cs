#nullable enable
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitMCPAddin.Core.ViewWorkspace
{
    internal static class ViewWorkspaceCapture
    {
        private static ViewWorkspaceXyz ToDto(XYZ p)
        {
            return new ViewWorkspaceXyz { X = p.X, Y = p.Y, Z = p.Z };
        }

        public static bool TryCapture(
            UIApplication uiapp,
            Document doc,
            string docKey,
            bool includeZoom,
            bool include3dOrientation,
            out ViewWorkspaceSnapshot? snapshot,
            out List<string> warnings,
            out string? error)
        {
            snapshot = null;
            warnings = new List<string>();
            error = null;

            try
            {
                var uidoc = uiapp != null ? uiapp.ActiveUIDocument : null;
                if (uidoc == null || uidoc.Document == null)
                {
                    error = "No active UIDocument.";
                    return false;
                }

                // Always capture the currently active document.
                // Note: some Revit API wrappers do not preserve reference equality across calls,
                // so comparing Document instances with ReferenceEquals can produce false negatives.
                doc = uidoc.Document;

                string docTitle = string.Empty;
                string docPath = string.Empty;
                try { docTitle = doc.Title ?? string.Empty; } catch { docTitle = string.Empty; }
                try { docPath = doc.PathName ?? string.Empty; } catch { docPath = string.Empty; }

                var snap = new ViewWorkspaceSnapshot
                {
                    SchemaVersion = "1.0",
                    SavedAtUtc = DateTime.UtcNow.ToString("o"),
                    DocKey = (docKey ?? string.Empty).Trim(),
                    DocTitle = docTitle,
                    DocPathHint = docPath
                };

                // Active view unique id
                try { snap.ActiveViewUniqueId = uidoc.ActiveView != null ? (uidoc.ActiveView.UniqueId ?? "") : ""; }
                catch { snap.ActiveViewUniqueId = ""; }

                // Open UI views
                IList<UIView> uivs = null;
                try { uivs = uidoc.GetOpenUIViews(); } catch { uivs = null; }
                if (uivs == null)
                {
                    warnings.Add("GetOpenUIViews returned null.");
                    snap.OpenViews = new List<ViewWorkspaceViewEntry>();
                    snapshot = snap;
                    return true;
                }

                var entries = new List<ViewWorkspaceViewEntry>();
                foreach (var uiv in uivs)
                {
                    try
                    {
                        if (uiv == null) continue;
                        var vid = uiv.ViewId;
                        var v = doc.GetElement(vid) as View;
                        if (v == null) continue;
                        if (v.IsTemplate) continue;

                        var entry = new ViewWorkspaceViewEntry
                        {
                            ViewUniqueId = v.UniqueId ?? "",
                            ViewIdInt = v.Id.IntValue(),
                            ViewName = v.Name ?? "",
                            ViewType = v.ViewType.ToString()
                        };

                        if (includeZoom)
                        {
                            try
                            {
                                var corners = uiv.GetZoomCorners();
                                if (corners != null && corners.Count >= 2)
                                {
                                    entry.Zoom = new ViewWorkspaceZoom { Corner1 = ToDto(corners[0]), Corner2 = ToDto(corners[1]) };
                                }
                                else
                                {
                                    entry.Zoom = null;
                                }
                            }
                            catch
                            {
                                entry.Zoom = null;
                            }
                        }

                        if (include3dOrientation)
                        {
                            try
                            {
                                var v3 = v as View3D;
                                if (v3 != null && !v3.IsTemplate)
                                {
                                    var o = v3.GetOrientation();
                                    entry.Orientation3D = new ViewWorkspaceOrientation3D
                                    {
                                        Eye = ToDto(o.EyePosition),
                                        Up = ToDto(o.UpDirection),
                                        Forward = ToDto(o.ForwardDirection),
                                        IsPerspective = false
                                    };
                                    try { entry.Orientation3D.IsPerspective = v3.IsPerspective; } catch { }
                                }
                            }
                            catch
                            {
                                entry.Orientation3D = null;
                            }
                        }

                        entries.Add(entry);
                    }
                    catch
                    {
                        // keep capture robust
                    }
                }

                snap.OpenViews = entries;
                snapshot = snap;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}

