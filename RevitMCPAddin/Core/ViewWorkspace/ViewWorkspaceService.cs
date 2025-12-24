#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Timers;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core.Ledger;

namespace RevitMCPAddin.Core.ViewWorkspace
{
    internal static class ViewWorkspaceService
    {
        private static readonly object _gate = new object();
        private static bool _initialized = false;

        private static ViewWorkspaceSettings _settings = new ViewWorkspaceSettings();
        private static Timer? _autosaveTimer;
        private static volatile bool _autosaveRequested = false;
        private static DateTime _lastAutosaveUtc = DateTime.MinValue;

        // Auto-restore set: docKey -> attempt count (best-effort)
        private static readonly Dictionary<string, int> _pendingRestoreAttempts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // Throttle auto-restore attempts (Idling can fire very frequently)
        private static readonly Dictionary<string, DateTime> _pendingRestoreLastAttemptUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        // Last seen UIApplication (captured on Idling)
        private static UIApplication? _lastUiapp;

        public static ViewWorkspaceSettings CurrentSettings
        {
            get { lock (_gate) { return _settings; } }
        }

        public static void Initialize(UIControlledApplication uiControlledApp)
        {
            if (uiControlledApp == null) return;

            lock (_gate)
            {
                if (_initialized) return;
                _initialized = true;
            }

            try { _settings = ViewWorkspaceSettings.Load(); } catch { _settings = new ViewWorkspaceSettings(); }

            try
            {
                var app = uiControlledApp.ControlledApplication;
                app.DocumentOpened += OnDocumentOpened;
                app.DocumentClosing += OnDocumentClosing;
            }
            catch (Exception ex)
            {
                RevitLogger.Warn("ViewWorkspaceService: event hook failed: " + ex.Message);
            }

            ApplyAutosaveTimer();
        }

        public static void Shutdown(UIControlledApplication uiControlledApp)
        {
            // Best-effort: save snapshot for the current active document, if any.
            try
            {
                var uiapp = _lastUiapp;
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc != null) TrySaveSnapshotForDoc(doc, reason: "OnShutdown");
            }
            catch { }

            try
            {
                var app = uiControlledApp?.ControlledApplication;
                if (app != null)
                {
                    app.DocumentOpened -= OnDocumentOpened;
                    app.DocumentClosing -= OnDocumentClosing;
                }
            }
            catch { }

            try
            {
                if (_autosaveTimer != null)
                {
                    _autosaveTimer.Stop();
                    _autosaveTimer.Dispose();
                }
            }
            catch { }
        }

        public static object SetAutosave(bool enabled, int intervalMinutes, int retention)
        {
            lock (_gate)
            {
                _settings.AutosaveEnabled = enabled;
                _settings.AutosaveIntervalMinutes = intervalMinutes;
                _settings.Retention = retention;
                if (_settings.AutosaveIntervalMinutes < 1) _settings.AutosaveIntervalMinutes = 1;
                if (_settings.Retention < 1) _settings.Retention = 1;
                if (_settings.Retention > 50) _settings.Retention = 50;
                try { _settings.Save(); } catch { }
            }

            ApplyAutosaveTimer();

            return new
            {
                ok = true,
                autosaveEnabled = enabled,
                intervalMinutes = intervalMinutes,
                retention = retention
            };
        }

        public static object SetAutoRestore(bool enabled)
        {
            lock (_gate)
            {
                _settings.AutoRestoreEnabled = enabled;
                try { _settings.Save(); } catch { }
            }
            return new { ok = true, autoRestoreEnabled = enabled };
        }

        public static object ResetToDefaults()
        {
            // Reset viewWorkspace settings to the code defaults and re-apply timers.
            lock (_gate)
            {
                _settings = new ViewWorkspaceSettings();
                try { _settings.Save(); } catch { }

                _autosaveRequested = false;
                _lastAutosaveUtc = DateTime.MinValue;
                _pendingRestoreAttempts.Clear();
            }

            ApplyAutosaveTimer();

            var s = CurrentSettings;
            return new
            {
                ok = true,
                autoRestoreEnabled = s.AutoRestoreEnabled,
                autosaveEnabled = s.AutosaveEnabled,
                autosaveIntervalMinutes = s.AutosaveIntervalMinutes,
                retention = s.Retention,
                includeZoom = s.IncludeZoom,
                include3dOrientation = s.Include3dOrientation
            };
        }

        private static void ApplyAutosaveTimer()
        {
            try
            {
                var s = CurrentSettings;
                if (!s.AutosaveEnabled)
                {
                    if (_autosaveTimer != null) _autosaveTimer.Stop();
                    return;
                }

                var intervalMs = Math.Max(60_000, s.AutosaveIntervalMinutes * 60_000);
                if (_autosaveTimer == null)
                {
                    _autosaveTimer = new Timer(intervalMs);
                    _autosaveTimer.AutoReset = true;
                    _autosaveTimer.Elapsed += (_, __) => { _autosaveRequested = true; };
                }
                _autosaveTimer.Interval = intervalMs;
                _autosaveTimer.Start();
            }
            catch (Exception ex)
            {
                RevitLogger.Warn("ViewWorkspace autosave timer failed: " + ex.Message);
            }
        }

        private static void OnDocumentOpened(object sender, DocumentOpenedEventArgs e)
        {
            try
            {
                var doc = e != null ? e.Document : null;
                if (doc == null) return;
                if (doc.IsFamilyDocument) return;

                // Ensure ledger exists so we can bind snapshots to a stable DocKey.
                if (!LedgerDocKeyProvider.TryGetOrCreateDocKey(doc, createIfMissing: true, out var docKey, out var dsId, out var err))
                {
                    RevitLogger.Warn("ViewWorkspace: DocKey resolve failed on DocumentOpened: " + (err ?? ""));
                    return;
                }

                var s = CurrentSettings;
                if (!s.AutoRestoreEnabled) return;

                lock (_gate)
                {
                    if (!_pendingRestoreAttempts.ContainsKey(docKey))
                    {
                        _pendingRestoreAttempts[docKey] = 0;
                        _pendingRestoreLastAttemptUtc[docKey] = DateTime.MinValue;
                    }
                }

                RevitLogger.Info($"ViewWorkspace: queued auto-restore docKey={docKey} storageId={(dsId.HasValue ? dsId.Value.ToString() : "?")}");
            }
            catch (Exception ex)
            {
                RevitLogger.Warn("ViewWorkspace: OnDocumentOpened failed: " + ex.Message);
            }
        }

        private static void OnDocumentClosing(object sender, DocumentClosingEventArgs e)
        {
            try
            {
                var doc = e != null ? e.Document : null;
                if (doc == null) return;
                TrySaveSnapshotForDoc(doc, reason: "DocumentClosing");
            }
            catch (Exception ex)
            {
                RevitLogger.Warn("ViewWorkspace: OnDocumentClosing failed: " + ex.Message);
            }
        }

        private static void TrySaveSnapshotForDoc(Document doc, string reason)
        {
            try
            {
                var uiapp = _lastUiapp;
                if (uiapp == null)
                {
                    RevitLogger.Warn($"ViewWorkspace: skip save ({reason}): UIApplication not available yet.");
                    return;
                }

                var uidoc = uiapp.ActiveUIDocument;
                if (uidoc == null || uidoc.Document == null)
                {
                    RevitLogger.Warn($"ViewWorkspace: skip save ({reason}): no active UIDocument.");
                    return;
                }

                var activeDoc = uidoc.Document;
                if (activeDoc == null)
                {
                    RevitLogger.Warn($"ViewWorkspace: skip save ({reason}): active UIDocument.Document is null.");
                    return;
                }

                // UI view capture is only supported for the active document.
                // Use Ledger doc_key comparison instead of ReferenceEquals to avoid false negatives.
                if (!LedgerDocKeyProvider.TryGetOrCreateDocKey(activeDoc, createIfMissing: true, out var activeDocKey, out var activeDsId, out var activeErr))
                {
                    RevitLogger.Warn($"ViewWorkspace: skip save ({reason}): active DocKey resolve failed: {activeErr}");
                    return;
                }

                if (!LedgerDocKeyProvider.TryGetOrCreateDocKey(doc, createIfMissing: true, out var closingDocKey, out var closingDsId, out var closingErr))
                {
                    RevitLogger.Warn($"ViewWorkspace: skip save ({reason}): DocKey resolve failed: {closingErr}");
                    return;
                }

                if (!string.Equals(activeDocKey, closingDocKey, StringComparison.OrdinalIgnoreCase))
                {
                    RevitLogger.Warn($"ViewWorkspace: skip save ({reason}): target doc is not the active document.");
                    return;
                }

                var docKey = activeDocKey;
                var dsId = activeDsId;
                doc = activeDoc;

                var s = CurrentSettings;

                if (!ViewWorkspaceCapture.TryCapture(uiapp, doc, docKey, s.IncludeZoom, s.Include3dOrientation, out var snap, out var warn, out var capErr))
                {
                    RevitLogger.Warn($"ViewWorkspace: capture failed ({reason}): {capErr}");
                    return;
                }

                if (!ViewWorkspaceStore.TrySaveToFile(snap!, s.Retention, out var savedPath, out var storeWarn, out var storeErr))
                {
                    RevitLogger.Warn($"ViewWorkspace: save failed ({reason}): {storeErr}");
                    return;
                }

                RevitLogger.Info($"ViewWorkspace: saved ({reason}) docKey={docKey} openViews={snap!.OpenViews.Count} path={savedPath}");
                foreach (var w in warn) RevitLogger.Info("ViewWorkspace: capture warn: " + w);
                foreach (var w in storeWarn) RevitLogger.Info("ViewWorkspace: store warn: " + w);
            }
            catch (Exception ex)
            {
                RevitLogger.Warn("ViewWorkspace: save snapshot failed: " + ex.Message);
            }
        }

        public static void OnIdling(UIApplication uiapp)
        {
            if (uiapp == null) return;
            _lastUiapp = uiapp;

            // Drive restore state machine first
            try
            {
                if (ViewWorkspaceRestoreCoordinator.IsActive)
                {
                    ViewWorkspaceRestoreCoordinator.Tick(uiapp);
                    return;
                }
            }
            catch { }

            // Auto-restore: start when the queued doc becomes active
            try
            {
                var uidoc = uiapp.ActiveUIDocument;
                var doc = uidoc != null ? uidoc.Document : null;
                if (doc != null && !doc.IsFamilyDocument)
                {
                    if (LedgerDocKeyProvider.TryGetDocKey(doc, out var docKey, out var dsId, out var err))
                    {
                        string? queued = null;
                        lock (_gate)
                        {
                            if (_pendingRestoreAttempts.ContainsKey(docKey))
                                queued = docKey;
                        }

                        if (!string.IsNullOrEmpty(queued))
                        {
                            bool skipAttempt = false;

                            // If snapshot file does not exist, do not spam retry logs.
                            // This typically means the project has never saved a workspace snapshot yet.
                            try
                            {
                                var expectedPath = ViewWorkspaceStore.GetWorkspaceFilePath(docKey);
                                bool canRestore = true;
                                string? skipReason = null;

                                if (!File.Exists(expectedPath))
                                {
                                    canRestore = false;
                                    skipReason = "snapshot file not found";
                                }
                                else
                                {
                                    // Also treat "cannot load" as missing (corrupted/invalid JSON), and capture a baseline snapshot to recover.
                                    try
                                    {
                                        if (!ViewWorkspaceStore.TryLoadFromFile(docKey, out var loaded, out var loadedPath, out var loadErr))
                                        {
                                            canRestore = false;
                                            skipReason = "snapshot load failed: " + (loadErr ?? "");
                                        }
                                    }
                                    catch
                                    {
                                        canRestore = false;
                                        skipReason = "snapshot load threw an exception";
                                    }
                                }

                                if (!canRestore)
                                {
                                    lock (_gate)
                                    {
                                        _pendingRestoreAttempts.Remove(docKey);
                                        _pendingRestoreLastAttemptUtc.Remove(docKey);
                                    }

                                    RevitLogger.Warn("ViewWorkspace: auto-restore skipped (" + (skipReason ?? "unknown") + "). docKey=" + docKey + " path=" + expectedPath + " -> capturing baseline snapshot now.");
                                    TrySaveSnapshotForDoc(doc, reason: "AutoRestoreSnapshotMissing");
                                    skipAttempt = true;
                                }
                            }
                            catch { /* ignore */ }

                            if (!skipAttempt)
                            {
                                // Throttle: avoid consuming all attempts within a single second.
                                bool allowAttempt = true;
                                try
                                {
                                    var nowUtc = DateTime.UtcNow;
                                    lock (_gate)
                                    {
                                        DateTime lastUtc;
                                        if (_pendingRestoreLastAttemptUtc.TryGetValue(docKey, out lastUtc))
                                        {
                                            if ((nowUtc - lastUtc).TotalMilliseconds < 500)
                                                allowAttempt = false;
                                        }
                                        if (allowAttempt) _pendingRestoreLastAttemptUtc[docKey] = nowUtc;
                                    }
                                }
                                catch { /* ignore */ }

                                if (allowAttempt)
                                {
                                    // Try a few times in case the document isn't ready for UI operations yet.
                                    int attempts;
                                    lock (_gate)
                                    {
                                        attempts = _pendingRestoreAttempts[docKey];
                                        _pendingRestoreAttempts[docKey] = attempts + 1;
                                    }

                                    if (attempts > 30)
                                    {
                                        lock (_gate)
                                        {
                                            _pendingRestoreAttempts.Remove(docKey);
                                            _pendingRestoreLastAttemptUtc.Remove(docKey);
                                        }
                                        RevitLogger.Warn("ViewWorkspace: auto-restore gave up (too many attempts). docKey=" + docKey);
                                    }
                                    else
                                    {
                                        if (TryStartRestoreForActiveDoc(uiapp, docKey, isAuto: true))
                                        {
                                            lock (_gate)
                                            {
                                                _pendingRestoreAttempts.Remove(docKey);
                                                _pendingRestoreLastAttemptUtc.Remove(docKey);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // Autosave: best-effort for active doc only
            try
            {
                var s = CurrentSettings;
                if (s.AutosaveEnabled && _autosaveRequested)
                {
                    // throttle (avoid too-frequent saves if idling is spammy)
                    var now = DateTime.UtcNow;
                    if (_lastAutosaveUtc != DateTime.MinValue && (now - _lastAutosaveUtc).TotalSeconds < 30)
                        return;

                    _autosaveRequested = false;
                    _lastAutosaveUtc = now;

                    var doc = uiapp.ActiveUIDocument != null ? uiapp.ActiveUIDocument.Document : null;
                    if (doc != null) TrySaveSnapshotForDoc(doc, reason: "Autosave");
                }
            }
            catch { }
        }

        private static bool TryStartRestoreForActiveDoc(UIApplication uiapp, string docKey, bool isAuto)
        {
            try
            {
                var uidoc = uiapp.ActiveUIDocument;
                var doc = uidoc != null ? uidoc.Document : null;
                if (doc == null) return false;

                if (!LedgerDocKeyProvider.TryGetDocKey(doc, out var actualKey, out var dsId, out var err))
                    return false;

                if (!string.Equals(actualKey, docKey, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (!ViewWorkspaceStore.TryLoadFromFile(docKey, out var snap, out var path, out var loadErr))
                {
                    if (isAuto)
                        RevitLogger.Warn("ViewWorkspace: auto-restore snapshot not found: docKey=" + docKey);
                    return false;
                }

                // DocKey must match
                if (!string.Equals((snap!.DocKey ?? "").Trim(), actualKey.Trim(), StringComparison.OrdinalIgnoreCase))
                    return false;

                // Warn if doc path differs (hint-only)
                try
                {
                    var docPath = doc.PathName ?? string.Empty;
                    var hint = snap.DocPathHint ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(docPath) && !string.IsNullOrWhiteSpace(hint)
                        && !string.Equals(docPath, hint, StringComparison.OrdinalIgnoreCase))
                    {
                        RevitLogger.Warn("ViewWorkspace: DocPath differs from snapshot hint (warning only).");
                    }
                }
                catch { }

                var s = CurrentSettings;
                if (!ViewWorkspaceRestoreCoordinator.Start(uiapp, snap, s.IncludeZoom, s.Include3dOrientation, activateSavedActiveView: true, out var sid, out var warn, out var startErr))
                    return false;

                RevitLogger.Info($"ViewWorkspace: restore started docKey={docKey} sessionId={sid} views={snap.OpenViews.Count}");
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
