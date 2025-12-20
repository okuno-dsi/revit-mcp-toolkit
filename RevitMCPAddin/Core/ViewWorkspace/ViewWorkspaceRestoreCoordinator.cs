#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.RevitUI;

namespace RevitMCPAddin.Core.ViewWorkspace
{
    internal static class ViewWorkspaceRestoreCoordinator
    {
        private sealed class RestoreSession
        {
            public string SessionId = Guid.NewGuid().ToString("N");
            public string DocKey = "";
            public string DocTitle = "";
            public string DocPath = "";
            public DateTime StartedUtc = DateTime.UtcNow;
            public bool IncludeZoom = true;
            public bool Include3d = true;
            public bool ActivateSavedActiveView = true;

            public string TargetActiveViewUniqueId = "";
            public List<ViewWorkspaceViewEntry> Views = new List<ViewWorkspaceViewEntry>();

            public int Index = 0;
            public int Phase = 0; // 0=activate, 1=apply, 2=final activate, 3=done
            public int StableTicksWait = 0;

            public int OpenedOrActivated = 0;
            public int AppliedZoom = 0;
            public int Applied3d = 0;
            public int MissingViews = 0;
            public List<string> Warnings = new List<string>();

            public bool Done = false;
            public string? Error = null;
        }

        private static readonly object _gate = new object();
        private static RestoreSession? _current;

        public static bool IsActive
        {
            get { lock (_gate) { return _current != null && !_current.Done; } }
        }

        public static object GetStatus()
        {
            lock (_gate)
            {
                if (_current == null)
                    return new { ok = true, active = false };

                var s = _current;
                return new
                {
                    ok = true,
                    active = !s.Done,
                    done = s.Done,
                    sessionId = s.SessionId,
                    docKey = s.DocKey,
                    docTitle = s.DocTitle,
                    startedAtUtc = s.StartedUtc.ToString("o"),
                    totalViews = s.Views.Count,
                    index = s.Index,
                    phase = s.Phase,
                    openedOrActivated = s.OpenedOrActivated,
                    appliedZoom = s.AppliedZoom,
                    applied3d = s.Applied3d,
                    missingViews = s.MissingViews,
                    warnings = s.Warnings.Count > 0 ? s.Warnings.ToArray() : null,
                    error = s.Error
                };
            }
        }

        public static bool Start(
            UIApplication uiapp,
            ViewWorkspaceSnapshot snapshot,
            bool includeZoom,
            bool include3dOrientation,
            bool activateSavedActiveView,
            out string? sessionId,
            out List<string> warnings,
            out string? error)
        {
            sessionId = null;
            warnings = new List<string>();
            error = null;

            if (uiapp == null) { error = "uiapp is null"; return false; }
            if (snapshot == null) { error = "snapshot is null"; return false; }
            if (string.IsNullOrWhiteSpace(snapshot.DocKey)) { error = "snapshot.doc_key is empty"; return false; }

            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc != null ? uidoc.Document : null;
            if (doc == null) { error = "No active document."; return false; }

            string title = string.Empty;
            string path = string.Empty;
            try { title = doc.Title ?? string.Empty; } catch { }
            try { path = doc.PathName ?? string.Empty; } catch { }

            var s = new RestoreSession
            {
                DocKey = snapshot.DocKey.Trim(),
                DocTitle = title,
                DocPath = path,
                IncludeZoom = includeZoom,
                Include3d = include3dOrientation,
                ActivateSavedActiveView = activateSavedActiveView,
                TargetActiveViewUniqueId = snapshot.ActiveViewUniqueId ?? "",
                Views = snapshot.OpenViews != null ? snapshot.OpenViews.ToList() : new List<ViewWorkspaceViewEntry>()
            };

            // Basic sanitization (skip empty entries)
            s.Views = s.Views
                .Where(v => v != null && (!string.IsNullOrWhiteSpace(v.ViewUniqueId) || v.ViewIdInt > 0))
                .ToList();

            lock (_gate)
            {
                // overwrite any existing session (best-effort)
                _current = s;
                sessionId = s.SessionId;
            }

            return true;
        }

        private static View? ResolveView(Document doc, ViewWorkspaceViewEntry entry)
        {
            if (doc == null || entry == null) return null;

            try
            {
                if (!string.IsNullOrWhiteSpace(entry.ViewUniqueId))
                {
                    var v0 = doc.GetElement(entry.ViewUniqueId) as View;
                    if (v0 != null) return v0;
                }
            }
            catch { }

            try
            {
                if (entry.ViewIdInt > 0)
                {
                    var v1 = doc.GetElement(new ElementId(entry.ViewIdInt)) as View;
                    if (v1 != null) return v1;
                }
            }
            catch { }

            return null;
        }

        private static ViewOrientation3D? BuildOrientation(ViewWorkspaceOrientation3D dto)
        {
            try
            {
                if (dto == null) return null;
                var eye = new XYZ(dto.Eye.X, dto.Eye.Y, dto.Eye.Z);
                var up = new XYZ(dto.Up.X, dto.Up.Y, dto.Up.Z);
                var fwd = new XYZ(dto.Forward.X, dto.Forward.Y, dto.Forward.Z);
                return new ViewOrientation3D(eye, up, fwd);
            }
            catch { return null; }
        }

        private static bool HasValidZoom(ViewWorkspaceZoom z)
        {
            try
            {
                if (z == null) return false;
                var dx = Math.Abs(z.Corner1.X - z.Corner2.X);
                var dy = Math.Abs(z.Corner1.Y - z.Corner2.Y);
                return dx > 1e-9 && dy > 1e-9;
            }
            catch { return false; }
        }

        public static void Tick(UIApplication uiapp)
        {
            RestoreSession? s;
            lock (_gate) { s = _current; }
            if (s == null || s.Done) return;

            try
            {
                var uidoc = uiapp != null ? uiapp.ActiveUIDocument : null;
                var doc = uidoc != null ? uidoc.Document : null;
                if (uidoc == null || doc == null)
                {
                    s.Error = "No active UIDocument.";
                    s.Done = true;
                    return;
                }

                // phase 3 = done
                if (s.Phase == 3)
                {
                    s.Done = true;
                    return;
                }

                // Phase 2: final active view restore
                if (s.Phase == 2)
                {
                    if (!s.ActivateSavedActiveView || string.IsNullOrWhiteSpace(s.TargetActiveViewUniqueId))
                    {
                        s.Phase = 3;
                        return;
                    }

                    try
                    {
                        var v = doc.GetElement(s.TargetActiveViewUniqueId) as View;
                        if (v != null && !v.IsTemplate)
                        {
                            try
                            {
                                if (uidoc.ActiveView == null || uidoc.ActiveView.Id.IntegerValue != v.Id.IntegerValue)
                                    uidoc.ActiveView = v;
                            }
                            catch
                            {
                                try { UiHelpers.TryRequestViewChange(uidoc, v); } catch { }
                            }
                        }
                        else
                        {
                            s.Warnings.Add("Saved active view not found; skipping.");
                        }
                    }
                    catch
                    {
                        s.Warnings.Add("Failed to activate saved active view; skipping.");
                    }

                    s.Phase = 3;
                    return;
                }

                // Finished all open views => proceed to final activation
                if (s.Index >= s.Views.Count)
                {
                    s.Phase = 2;
                    return;
                }

                var entry = s.Views[s.Index];
                var target = ResolveView(doc, entry);
                if (target == null || target.IsTemplate)
                {
                    s.MissingViews++;
                    s.Warnings.Add($"View missing/skipped: uniqueId={entry?.ViewUniqueId} id={entry?.ViewIdInt} name={entry?.ViewName}");
                    s.Index++;
                    s.Phase = 0;
                    s.StableTicksWait = 0;
                    return;
                }

                // Phase 0: activate view
                if (s.Phase == 0)
                {
                    try
                    {
                        if (uidoc.ActiveView == null || uidoc.ActiveView.Id.IntegerValue != target.Id.IntegerValue)
                        {
                            try
                            {
                                uidoc.ActiveView = target;
                            }
                            catch
                            {
                                try { UiHelpers.TryRequestViewChange(uidoc, target); } catch { }
                            }
                        }
                        s.OpenedOrActivated++;
                    }
                    catch (Exception ex)
                    {
                        s.Warnings.Add($"Activate view failed: {target.Id.IntegerValue} {target.Name}: {ex.Message}");
                    }

                    // give UI a tick to stabilize
                    s.StableTicksWait = 0;
                    s.Phase = 1;
                    return;
                }

                // Phase 1: apply zoom / 3d orientation (best-effort)
                if (s.Phase == 1)
                {
                    // Ensure active view is the one we expect (some environments need extra ticks)
                    try
                    {
                        if (uidoc.ActiveView == null || uidoc.ActiveView.Id.IntegerValue != target.Id.IntegerValue)
                        {
                            s.StableTicksWait++;
                            if (s.StableTicksWait < 3) return;
                        }
                    }
                    catch { }

                    if (s.IncludeZoom && entry.Zoom != null && HasValidZoom(entry.Zoom))
                    {
                        try
                        {
                            UIView? uiv = null;
                            try
                            {
                                var open = uidoc.GetOpenUIViews();
                                if (open != null)
                                    uiv = open.FirstOrDefault(x => x != null && x.ViewId.IntegerValue == target.Id.IntegerValue);
                            }
                            catch { uiv = null; }

                            if (uiv != null)
                            {
                                var c1 = new XYZ(entry.Zoom.Corner1.X, entry.Zoom.Corner1.Y, entry.Zoom.Corner1.Z);
                                var c2 = new XYZ(entry.Zoom.Corner2.X, entry.Zoom.Corner2.Y, entry.Zoom.Corner2.Z);
                                uiv.ZoomAndCenterRectangle(c1, c2);
                                s.AppliedZoom++;
                            }
                        }
                        catch (Exception ex)
                        {
                            s.Warnings.Add($"Zoom restore failed: {target.Id.IntegerValue} {target.Name}: {ex.Message}");
                        }
                    }

                    if (s.Include3d && entry.Orientation3D != null)
                    {
                        try
                        {
                            var v3 = target as View3D;
                            if (v3 != null && !v3.IsTemplate)
                            {
                                var o = BuildOrientation(entry.Orientation3D);
                                if (o != null)
                                {
                                    v3.SetOrientation(o);
                                    s.Applied3d++;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            s.Warnings.Add($"3D orientation restore failed: {target.Id.IntegerValue} {target.Name}: {ex.Message}");
                        }
                    }

                    s.Index++;
                    s.Phase = 0;
                    s.StableTicksWait = 0;
                    return;
                }
            }
            catch (Exception ex)
            {
                s.Error = ex.Message;
                s.Done = true;
            }
        }
    }
}
