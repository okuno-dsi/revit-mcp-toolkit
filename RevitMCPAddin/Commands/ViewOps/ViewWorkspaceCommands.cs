// RevitMCPAddin/Commands/ViewOps/ViewWorkspaceCommands.cs
#nullable enable
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.Core.Ledger;
using RevitMCPAddin.Core.ViewWorkspace;

namespace RevitMCPAddin.Commands.ViewOps
{
    public class SaveViewWorkspaceCommand : IRevitCommandHandler
    {
        public string CommandName => "save_view_workspace";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return ResultUtil.Err("No active document.");

            var p = cmd.Params as JObject ?? new JObject();

            // Optional strict doc_key targeting
            string requestedDocKey =
                p.Value<string>("doc_key")
                ?? p.Value<string>("docKey")
                ?? "";
            requestedDocKey = requestedDocKey.Trim();

            if (!LedgerDocKeyProvider.TryGetOrCreateDocKey(doc, createIfMissing: true, out var docKey, out var dsId, out var dkErr))
                return ResultUtil.Err(new { msg = dkErr ?? "DocKey resolve failed.", code = "DOC_KEY_UNAVAILABLE" });

            if (!string.IsNullOrEmpty(requestedDocKey) && !string.Equals(requestedDocKey, docKey, StringComparison.OrdinalIgnoreCase))
                return ResultUtil.Err(new { msg = "DocKey mismatch. Wrong project may be open.", code = "DOC_KEY_MISMATCH", expected = requestedDocKey, actual = docKey });

            var s = ViewWorkspaceService.CurrentSettings;

            bool includeZoom =
                p.Value<bool?>("include_zoom")
                ?? p.Value<bool?>("includeZoom")
                ?? s.IncludeZoom;

            bool include3d =
                p.Value<bool?>("include_3d_orientation")
                ?? p.Value<bool?>("include3dOrientation")
                ?? s.Include3dOrientation;

            string sink =
                (p.Value<string>("sink") ?? "file").Trim().ToLowerInvariant();

            if (sink != "file")
                return ResultUtil.Err(new { msg = "Only sink='file' is supported in this build.", code = "SINK_NOT_SUPPORTED", sink });

            int retention =
                p.Value<int?>("retention")
                ?? s.Retention;
            if (retention < 1) retention = 1;
            if (retention > 50) retention = 50;

            if (!ViewWorkspaceCapture.TryCapture(uiapp, doc, docKey, includeZoom, include3d, out var snap, out var capWarn, out var capErr))
                return ResultUtil.Err(new { msg = capErr ?? "Capture failed.", code = "CAPTURE_FAILED" });

            if (!ViewWorkspaceStore.TrySaveToFile(snap!, retention, out var savedPath, out var storeWarn, out var storeErr))
                return ResultUtil.Err(new { msg = storeErr ?? "Save failed.", code = "SAVE_FAILED" });

            var warnings = new List<string>();
            warnings.AddRange(capWarn);
            warnings.AddRange(storeWarn);

            return new
            {
                ok = true,
                doc_key = docKey,
                dataStorageId = dsId,
                sink = "file",
                savedPath,
                savedAtUtc = snap!.SavedAtUtc,
                openViewCount = snap.OpenViews != null ? snap.OpenViews.Count : 0,
                activeViewUniqueId = snap.ActiveViewUniqueId,
                warnings = warnings.Count > 0 ? warnings.ToArray() : null
            };
        }
    }

    public class RestoreViewWorkspaceCommand : IRevitCommandHandler
    {
        public string CommandName => "restore_view_workspace";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return ResultUtil.Err("No active document.");

            var p = cmd.Params as JObject ?? new JObject();

            string requestedDocKey =
                p.Value<string>("doc_key")
                ?? p.Value<string>("docKey")
                ?? "";
            requestedDocKey = requestedDocKey.Trim();

            if (!LedgerDocKeyProvider.TryGetOrCreateDocKey(doc, createIfMissing: true, out var docKey, out var dsId, out var dkErr))
                return ResultUtil.Err(new { msg = dkErr ?? "DocKey resolve failed.", code = "DOC_KEY_UNAVAILABLE" });

            if (!string.IsNullOrEmpty(requestedDocKey) && !string.Equals(requestedDocKey, docKey, StringComparison.OrdinalIgnoreCase))
                return ResultUtil.Err(new { msg = "DocKey mismatch. Wrong project may be open.", code = "DOC_KEY_MISMATCH", expected = requestedDocKey, actual = docKey });

            string source =
                (p.Value<string>("source") ?? "file").Trim().ToLowerInvariant();
            if (source != "file" && source != "auto")
                return ResultUtil.Err(new { msg = "Only source='file'|'auto' is supported in this build.", code = "SOURCE_NOT_SUPPORTED", source });

            if (!ViewWorkspaceStore.TryLoadFromFile(docKey, out var snap, out var path, out var loadErr))
                return ResultUtil.Err(new { msg = loadErr ?? "Snapshot not found.", code = "SNAPSHOT_NOT_FOUND", doc_key = docKey });

            // Verify DocKey binding
            if (!string.Equals((snap!.DocKey ?? "").Trim(), docKey.Trim(), StringComparison.OrdinalIgnoreCase))
                return ResultUtil.Err(new { msg = "DocKey mismatch. Snapshot belongs to a different project.", code = "DOC_KEY_MISMATCH", expected = docKey, actual = snap.DocKey });

            var warnings = new List<string>();
            try
            {
                var docPath = doc.PathName ?? string.Empty;
                var hint = snap.DocPathHint ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(docPath) && !string.IsNullOrWhiteSpace(hint)
                    && !string.Equals(docPath, hint, StringComparison.OrdinalIgnoreCase))
                    warnings.Add("DocPath differs from snapshot doc_path_hint (warning only).");
            }
            catch { }

            var s = ViewWorkspaceService.CurrentSettings;
            bool includeZoom =
                p.Value<bool?>("include_zoom")
                ?? p.Value<bool?>("includeZoom")
                ?? s.IncludeZoom;

            bool include3d =
                p.Value<bool?>("include_3d_orientation")
                ?? p.Value<bool?>("include3dOrientation")
                ?? s.Include3dOrientation;

            bool activateSavedActiveView =
                p.Value<bool?>("activate_saved_active_view")
                ?? p.Value<bool?>("activateSavedActiveView")
                ?? true;

            if (!ViewWorkspaceRestoreCoordinator.Start(uiapp, snap, includeZoom, include3d, activateSavedActiveView, out var sessionId, out var startWarn, out var startErr))
                return ResultUtil.Err(new { msg = startErr ?? "Restore start failed.", code = "RESTORE_START_FAILED" });

            warnings.AddRange(startWarn);

            return new
            {
                ok = true,
                msg = "Accepted. Restoring view workspace asynchronously via Idling.",
                doc_key = docKey,
                dataStorageId = dsId,
                source = "file",
                snapshotPath = path,
                restoreSessionId = sessionId,
                openViews = snap.OpenViews != null ? snap.OpenViews.Count : 0,
                activeViewUniqueId = snap.ActiveViewUniqueId,
                warnings = warnings.Count > 0 ? warnings.ToArray() : null
            };
        }
    }

    public class SetViewWorkspaceAutosaveCommand : IRevitCommandHandler
    {
        public string CommandName => "set_view_workspace_autosave";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = cmd.Params as JObject ?? new JObject();

            bool enabled = p.Value<bool?>("enabled") ?? false;

            int intervalMinutes =
                p.Value<int?>("interval_minutes")
                ?? p.Value<int?>("intervalMinutes")
                ?? 5;

            int retention =
                p.Value<int?>("retention")
                ?? 10;

            // Optional: also allow toggling auto restore from the same command (convenience)
            bool? autoRestoreEnabled =
                p.Value<bool?>("auto_restore_enabled")
                ?? p.Value<bool?>("autoRestoreEnabled");

            var res = ViewWorkspaceService.SetAutosave(enabled, intervalMinutes, retention);

            if (autoRestoreEnabled.HasValue)
                ViewWorkspaceService.SetAutoRestore(autoRestoreEnabled.Value);

            var s = ViewWorkspaceService.CurrentSettings;
            return new
            {
                ok = true,
                autosaveEnabled = s.AutosaveEnabled,
                autosaveIntervalMinutes = s.AutosaveIntervalMinutes,
                retention = s.Retention,
                autoRestoreEnabled = s.AutoRestoreEnabled
            };
        }
    }

    public class GetViewWorkspaceRestoreStatusCommand : IRevitCommandHandler
    {
        public string CommandName => "get_view_workspace_restore_status";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            return ViewWorkspaceRestoreCoordinator.GetStatus();
        }
    }
}

