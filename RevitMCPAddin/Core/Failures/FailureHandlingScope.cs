#nullable enable
using System;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace RevitMCPAddin.Core.Failures
{
    /// <summary>
    /// Command-scoped failure handler (best-effort).
    /// - Uses a whitelist of FailureDefinitionId to decide what can be auto-deleted/resolved.
    /// - Defaults to conservative rollback for non-whitelisted failures.
    /// - Captures failures/dialogs for agent-friendly diagnostics.
    /// </summary>
    internal sealed class FailureHandlingScope : System.IDisposable
    {
        private readonly UIApplication _uiapp;
        private readonly Autodesk.Revit.ApplicationServices.Application _app;
        private readonly FailureHandlingMode _mode;
        private readonly System.IDisposable? _ctxScope;
        private readonly bool _dismissDialogs;

        private readonly FailureWhitelistLoadStatus _whitelistStatus;
        private readonly FailureWhitelistIndex? _whitelistIndex;

        private readonly EventHandler<FailuresProcessingEventArgs> _failHandler;
        private readonly EventHandler<DialogBoxShowingEventArgs> _dlgHandler;

        private bool _disposed;

        public RevitMCPAddin.Core.Failures.CommandIssues Issues { get; private set; }

        public FailureHandlingMode Mode { get { return _mode; } }
        public FailureWhitelistLoadStatus WhitelistStatus { get { return _whitelistStatus; } }

        public FailureHandlingScope(UIApplication uiapp, FailureHandlingMode mode)
        {
            if (uiapp == null) throw new ArgumentNullException(nameof(uiapp));

            _uiapp = uiapp;
            _app = uiapp.Application;
            _mode = mode;

            // Load whitelist (best-effort).
            _whitelistStatus = FailureWhitelistService.GetStatus();
            FailureWhitelistIndex tmp;
            if (FailureWhitelistService.TryGetIndex(out tmp))
                _whitelistIndex = tmp;

            // In automation, modal dialogs block subsequent commands and can deadlock the job queue.
            // Default to dismiss dialogs whenever failureHandling is enabled.
            _dismissDialogs = (mode != FailureHandlingMode.Off);

            Issues = new RevitMCPAddin.Core.Failures.CommandIssues();
            try { _ctxScope = FailureHandlingContext.Push(_mode, Issues); } catch { _ctxScope = null; }

            _failHandler = new EventHandler<FailuresProcessingEventArgs>(OnFailuresProcessing);
            _dlgHandler = new EventHandler<DialogBoxShowingEventArgs>(OnDialogShowing);

            _app.FailuresProcessing += _failHandler;
            _uiapp.DialogBoxShowing += _dlgHandler;
        }

        private void OnFailuresProcessing(object sender, FailuresProcessingEventArgs e)
        {
            var fa = e.GetFailuresAccessor();
            var msgs = fa.GetFailureMessages();
            if (msgs == null || msgs.Count == 0)
            {
                e.SetProcessingResult(FailureProcessingResult.Continue);
                return;
            }

            // When mode=Off, this scope is "capture-only": do not delete/resolve/rollback.
            bool captureOnly = (_mode == FailureHandlingMode.Off);
            bool requestRollback = false;

            foreach (FailureMessageAccessor f in msgs)
            {
                var defId = f.GetFailureDefinitionId();
                var sev = f.GetSeverity();

                string action = "none";
                bool whitelisted = false;
                string ruleId = string.Empty;
                FailureWhitelistRule? matchedRule = null;

                FailureWhitelistRule rule;
                if (_whitelistIndex != null && _whitelistIndex.TryGetRule(defId.Guid, out rule) && rule != null && rule.Enabled)
                {
                    if ((sev == FailureSeverity.Warning && rule.MatchWarning) ||
                        (sev == FailureSeverity.Error && rule.MatchError))
                    {
                        whitelisted = true;
                        ruleId = rule.Id ?? string.Empty;
                        matchedRule = rule;
                    }
                }

                if (captureOnly)
                {
                    action = "capture_only";
                }
                else
                {
                    // Conservative default: rollback on any non-whitelisted failure.
                    if (_mode == FailureHandlingMode.Rollback)
                    {
                        requestRollback = true;
                        action = "rollback";
                    }
                    else if (!whitelisted)
                    {
                        requestRollback = true;
                        action = "rollback_unwhitelisted";
                    }
                    else
                    {
                        if (matchedRule == null)
                        {
                            requestRollback = true;
                            action = "rollback_unexpected_no_rule";
                        }
                        else
                        {
                            FailureWhitelistRule wlRule = matchedRule;
                            // Whitelisted: apply selected mode, but still respect rule action allowlist.
                            if (sev == FailureSeverity.Warning)
                            {
                                if (_mode == FailureHandlingMode.Delete)
                                {
                                    if (wlRule.AllowDeleteWarning)
                                    {
                                        try
                                        {
                                            fa.DeleteWarning(f);
                                            action = "delete_warning";
                                        }
                                        catch
                                        {
                                            requestRollback = true;
                                            action = "rollback_delete_warning_failed";
                                        }
                                    }
                                    else
                                    {
                                        requestRollback = true;
                                        action = "rollback_delete_not_allowed";
                                    }
                                }
                                else if (_mode == FailureHandlingMode.Resolve)
                                {
                                    bool resolved = false;
                                    if (wlRule.AllowResolve)
                                    {
                                        resolved = TryResolveFailureBestEffort(fa, f);
                                        action = resolved ? "resolve" : "resolve_failed";
                                    }

                                    if (!resolved)
                                    {
                                        if (wlRule.AllowDeleteWarning)
                                        {
                                            try
                                            {
                                                fa.DeleteWarning(f);
                                                action = action + "|delete_warning";
                                            }
                                            catch
                                            {
                                                requestRollback = true;
                                                action = "rollback_warning_unhandled";
                                            }
                                        }
                                        else
                                        {
                                            requestRollback = true;
                                            action = "rollback_warning_unhandled";
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // Error / others
                                if (_mode == FailureHandlingMode.Resolve && wlRule.AllowResolve)
                                {
                                    bool resolved = TryResolveFailureBestEffort(fa, f);
                                    if (resolved)
                                    {
                                        action = "resolve";
                                    }
                                    else
                                    {
                                        requestRollback = true;
                                        action = "rollback_error_resolve_failed";
                                    }
                                }
                                else
                                {
                                    requestRollback = true;
                                    action = "rollback_error";
                                }
                            }
                        }
                    }
                }

                // Capture record (agent-friendly).
                try
                {
                    bool includeDesc = (_whitelistIndex == null || _whitelistIndex.IncludeDescriptionText);
                    if (!includeDesc)
                    {
                        // Always record details when rollback is involved (for debugging and auditability).
                        if (!string.IsNullOrWhiteSpace(action) && action.StartsWith("rollback", StringComparison.OrdinalIgnoreCase))
                            includeDesc = true;
                        else if (sev == FailureSeverity.Error)
                            includeDesc = true;
                    }
                    var msg = includeDesc ? f.GetDescriptionText() : string.Empty;

                    int[] ids = new int[0];
                    if (_whitelistIndex == null || _whitelistIndex.IncludeElements)
                    {
                        try
                        {
                            var failing = f.GetFailingElementIds();
                            if (failing != null)
                            {
                                var max = _whitelistIndex != null ? _whitelistIndex.MaxElementIdsPerFailure : 50;
                                if (max <= 0) ids = new int[0];
                                else ids = failing.Take(max).Select(x => x.IntValue()).ToArray();
                            }
                        }
                        catch { /* ignore */ }
                    }

                    Issues.failures.Add(new RevitMCPAddin.Core.Failures.FailureRecord
                    {
                        id = defId.ToString(),
                        idGuid = defId.Guid.ToString(),
                        severity = sev.ToString(),
                        message = msg,
                        elementIds = ids,
                        action = action,
                        whitelisted = whitelisted,
                        ruleId = ruleId
                    });
                }
                catch { /* ignore */ }
            }

            if (!captureOnly && requestRollback)
            {
                try
                {
                    var opts = fa.GetFailureHandlingOptions();
                    opts.SetClearAfterRollback(true);
                    fa.SetFailureHandlingOptions(opts);
                }
                catch { /* ignore */ }

                try
                {
                    Issues.rollbackRequested = true;
                    if (string.IsNullOrWhiteSpace(Issues.rollbackReason))
                        Issues.rollbackReason = "FailuresProcessing.ProceedWithRollBack";
                }
                catch { /* ignore */ }

                e.SetProcessingResult(FailureProcessingResult.ProceedWithRollBack);
                return;
            }

            e.SetProcessingResult(FailureProcessingResult.Continue);
        }

        private void OnDialogShowing(object sender, DialogBoxShowingEventArgs e)
        {
            // Log dialog + optionally dismiss to avoid blocking automation.
            string dialogId = string.Empty;
            try { dialogId = e.DialogId ?? string.Empty; } catch { dialogId = string.Empty; }

            string message = string.Empty;
            try
            {
                var prop = e.GetType().GetProperty("Message", BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.PropertyType == typeof(string))
                    message = (string)(prop.GetValue(e, null) ?? string.Empty);
            }
            catch { /* ignore */ }

            bool dismissed = false;
            int overrideResult = 0;

            if (_dismissDialogs)
            {
                // Default to OK/Close-equivalent. For Revit warning dialogs this usually maps to "OK".
                // We still capture the dialogId/message so the agent can report what was dismissed.
                try
                {
                    overrideResult = 1;
                    e.OverrideResult(overrideResult);
                    dismissed = true;
                }
                catch { /* ignore */ }
            }

            try
            {
                Issues.dialogs.Add(new RevitMCPAddin.Core.Failures.DialogRecord
                {
                    dialogId = dialogId,
                    message = message,
                    dismissed = dismissed,
                    overrideResult = overrideResult
                });
            }
            catch { /* ignore */ }
        }

        private static bool TryResolveFailureBestEffort(FailuresAccessor fa, FailureMessageAccessor f)
        {
            // Reflection-based to reduce API version coupling.
            try
            {
                // If a default resolution type exists, set it.
                try
                {
                    var getDefault = f.GetType().GetMethod("GetDefaultResolutionType", BindingFlags.Public | BindingFlags.Instance);
                    if (getDefault != null)
                    {
                        var def = getDefault.Invoke(f, null);
                        if (def != null)
                        {
                            var setCur = f.GetType().GetMethod("SetCurrentResolutionType", BindingFlags.Public | BindingFlags.Instance);
                            if (setCur != null)
                            {
                                setCur.Invoke(f, new object[] { def });
                            }
                        }
                    }
                }
                catch { /* ignore */ }

                // Try FailuresAccessor.ResolveFailure(FailureMessageAccessor)
                try
                {
                    var m = fa.GetType().GetMethod("ResolveFailure", BindingFlags.Public | BindingFlags.Instance);
                    if (m != null)
                    {
                        m.Invoke(fa, new object[] { f });
                        return true;
                    }
                }
                catch { /* ignore */ }

                // Try FailuresAccessor.ResolveFailures(FailureResolutionType)
                try
                {
                    var getDefault = f.GetType().GetMethod("GetDefaultResolutionType", BindingFlags.Public | BindingFlags.Instance);
                    if (getDefault != null)
                    {
                        var def = getDefault.Invoke(f, null);
                        if (def != null)
                        {
                            var m = fa.GetType().GetMethod("ResolveFailures", BindingFlags.Public | BindingFlags.Instance);
                            if (m != null)
                            {
                                m.Invoke(fa, new object[] { def });
                                return true;
                            }
                        }
                    }
                }
                catch { /* ignore */ }
            }
            catch { /* ignore */ }

            return false;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~FailureHandlingScope()
        {
            Dispose(false);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                try { _app.FailuresProcessing -= _failHandler; } catch { /* ignore */ }
                try { _uiapp.DialogBoxShowing -= _dlgHandler; } catch { /* ignore */ }
                try { _ctxScope?.Dispose(); } catch { /* ignore */ }
            }
            _disposed = true;
        }
    }
}
